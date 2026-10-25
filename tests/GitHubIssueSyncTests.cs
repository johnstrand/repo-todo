using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RepoTodo.Data;
using RepoTodo.Services;

namespace RepoTodo.Tests;

[TestClass]
public sealed class GitHubIssueSyncTests
{
    private SqliteConnection _connection = null!;
    private TodoContext _context = null!;
    private int _repoId;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TodoContext>().UseSqlite(_connection).Options;
        _context = new TodoContext(options);
        await _context.Database.EnsureCreatedAsync();
        _repoId = await new RepositoryService(_context).EnsureRepoExists("github-sync-test", _ => false);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [TestMethod]
    public void GitHubRemoteParserSupportsHttpsAndSshUrls()
    {
        Assert.AreEqual(new GitHubRepository("owner", "repo"), GitHubRepository.FromRemoteUrls(
            [Remote("origin", "https://github.com/owner/repo.git")]));
        Assert.AreEqual(new GitHubRepository("owner", "repo"), GitHubRepository.FromRemoteUrls(
            [Remote("origin", "git@github.com:owner/repo.git")]));
        Assert.AreEqual(new GitHubRepository("owner", "repo"), GitHubRepository.FromRemoteUrls(
            [Remote("backup", "https://example.com/other/repo.git"), Remote("origin", "ssh://git@github.com/owner/repo.git")]));
        Assert.IsNull(GitHubRepository.FromRemoteUrls([Remote("origin", "https://example.com/owner/repo.git")]));
    }

    [TestMethod]
    public async Task SyncPublishesLocalTodosAsManagedIssuesAndStoresMapping()
    {
        var item = new TodoItem
        {
            RepoId = _repoId,
            Title = "Deploy release",
            Body = "Check rollback plan",
            Status = TodoItemStatus.InProgress,
            Tags = ["ops", "release"],
            Subtasks = [new TodoItemSubtask { Title = "Notify team", Status = TodoItemStatus.Completed }]
        };
        _context.TodoItems.Add(item);
        await _context.SaveChangesAsync();

        string? createdBody = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/issues", StringComparison.Ordinal))
            {
                return JsonResponse("[]");
            }

            if (request.Method == HttpMethod.Get && path.Contains("/labels/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/labels", StringComparison.Ordinal))
            {
                var label = await ReadJsonAsync(request);
                return JsonResponse(JsonSerializer.Serialize(new { name = label.GetProperty("name").GetString() }));
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/issues", StringComparison.Ordinal))
            {
                var issue = await ReadJsonAsync(request);
                createdBody = issue.GetProperty("body").GetString();
                Assert.AreEqual("Deploy release", issue.GetProperty("title").GetString());
                var labels = issue.GetProperty("labels").EnumerateArray().Select(value => value.GetString()).ToArray();
                CollectionAssert.Contains(labels, "repotodo");
                CollectionAssert.Contains(labels, "repotodo-status-inprogress");
                CollectionAssert.Contains(labels, "repotodo-tag-ops");
                CollectionAssert.Contains(labels, "repotodo-tag-release");
                return JsonResponse("""{"number":81,"title":"Deploy release","body":"","state":"open","labels":[]}""");
            }

            Assert.Fail($"Unexpected GitHub API request: {request.Method} {request.RequestUri}");
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var client = new HttpClient(handler);
        var result = await CreateService(client).SyncAsync();

        Assert.AreEqual(1, result.Published);
        Assert.AreEqual(0, result.Imported);
        Assert.IsEmpty(result.Conflicts);
        Assert.IsNotNull(createdBody);
        StringAssert.Contains(createdBody, "Check rollback plan");
        StringAssert.Contains(createdBody, "- [x] Notify team");

        var syncState = await _context.TodoGitHubSyncStates.SingleAsync();
        Assert.AreEqual(item.TodoItemId, syncState.TodoItemId);
        Assert.AreEqual(81, syncState.GitHubIssueNumber);
        Assert.IsFalse(syncState.IsDeleted);
    }

    [TestMethod]
    public async Task SyncCreatesCompletedTodoIssueThenClosesIt()
    {
        var item = new TodoItem
        {
            RepoId = _repoId,
            Title = "Already complete",
            Body = "",
            Status = TodoItemStatus.Completed
        };
        _context.TodoItems.Add(item);
        await _context.SaveChangesAsync();

        var closedIssue = false;
        var handler = new StubHttpMessageHandler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/issues", StringComparison.Ordinal))
            {
                return JsonResponse("[]");
            }

            if (request.Method == HttpMethod.Get && path.Contains("/labels/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/labels", StringComparison.Ordinal))
            {
                var label = await ReadJsonAsync(request);
                return JsonResponse(JsonSerializer.Serialize(new { name = label.GetProperty("name").GetString() }));
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/issues", StringComparison.Ordinal))
            {
                var payload = await ReadJsonAsync(request);
                Assert.IsFalse(payload.TryGetProperty("state", out _));
                return JsonResponse("""{"number":82,"title":"Already complete","body":"","state":"open","labels":[]}""");
            }

            if (request.Method == HttpMethod.Patch && path.EndsWith("/issues/82", StringComparison.Ordinal))
            {
                var payload = await ReadJsonAsync(request);
                closedIssue = payload.GetProperty("state").GetString() == "closed";
                return JsonResponse("{} ");
            }

            Assert.Fail($"Unexpected GitHub API request: {request.Method} {request.RequestUri}");
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var client = new HttpClient(handler);
        var result = await CreateService(client).SyncAsync();

        Assert.AreEqual(1, result.Published);
        Assert.IsTrue(closedIssue);
    }

    [TestMethod]
    public async Task SyncImportsManagedIssueAndReportsConcurrentEditsWithoutOverwritingEitherSide()
    {
        var remoteTitle = "Shared todo";
        var localService = new TodoService(_context, _repoId);
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/issues", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(ManagedIssue(52, remoteTitle, "Initial body")));
            }

            Assert.Fail($"Unexpected GitHub API request: {request.Method} {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });
        using var client = new HttpClient(handler);
        var sync = CreateService(client);

        var initial = await sync.SyncAsync();
        Assert.AreEqual(1, initial.Imported);
        var item = (await localService.ListTodoItems(TodoItemStatus.NotStarted)).Single();
        Assert.AreEqual("Shared todo", item.Title);

        await localService.UpdateTodoItem(item.TodoItemId, title: "Local edit");
        remoteTitle = "Remote edit";

        var conflicted = await sync.SyncAsync();
        Assert.HasCount(1, conflicted.Conflicts);
        Assert.AreEqual("Local edit", (await localService.GetTodoItem(item.TodoItemId))!.Title);
        Assert.AreEqual(52, (await _context.TodoGitHubSyncStates.SingleAsync()).GitHubIssueNumber);
    }

    [TestMethod]
    public async Task LocalOnlyChangesUpdateLinkedIssueAndPreserveUnmanagedLabels()
    {
        var item = new TodoItem
        {
            RepoId = _repoId,
            Title = "Local update",
            Body = "New body",
            Status = TodoItemStatus.Ready
        };
        _context.TodoItems.Add(item);
        await _context.SaveChangesAsync();
        _context.TodoGitHubSyncStates.Add(new TodoGitHubSyncState
        {
            RepoId = _repoId,
            TodoItemId = item.TodoItemId,
            GitHubIssueNumber = 56,
            LastSyncedSnapshot = "{\"title\":\"Original\",\"body\":\"Old body\",\"status\":0,\"tags\":[],\"subtasks\":[]}"
        });
        await _context.SaveChangesAsync();

        string? patchedIssue = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            Assert.AreEqual("Bearer test-token", request.Headers.Authorization?.ToString());
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/issues", StringComparison.Ordinal))
            {
                return JsonResponse(ManagedIssue(56, "Original", "Old body", "triage"));
            }

            if (request.Method == HttpMethod.Get && path.Contains("/labels/", StringComparison.Ordinal))
            {
                var name = Uri.UnescapeDataString(path[(path.LastIndexOf("/labels/", StringComparison.Ordinal) + 8)..]);
                return JsonResponse(JsonSerializer.Serialize(new { name }));
            }

            if (request.Method == HttpMethod.Patch && path.EndsWith("/issues/56", StringComparison.Ordinal))
            {
                patchedIssue = await request.Content!.ReadAsStringAsync();
                return JsonResponse("{} ");
            }

            Assert.Fail($"Unexpected GitHub API request: {request.Method} {request.RequestUri}");
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        using var client = new HttpClient(handler);

        var result = await CreateService(client).SyncAsync();

        Assert.AreEqual(1, result.Published);
        Assert.IsEmpty(result.Conflicts);
        Assert.IsNotNull(patchedIssue);
        using var patch = JsonDocument.Parse(patchedIssue);
        Assert.AreEqual("Local update", patch.RootElement.GetProperty("title").GetString());
        Assert.AreEqual("New body", patch.RootElement.GetProperty("body").GetString()!
            .Split("<!-- repotodo:subtasks -->")[0].TrimEnd('\r', '\n'));
        var labels = patch.RootElement.GetProperty("labels").EnumerateArray().Select(label => label.GetString()).ToArray();
        CollectionAssert.Contains(labels, "triage");
        CollectionAssert.Contains(labels, "repotodo");
        CollectionAssert.Contains(labels, "repotodo-status-ready");
    }

    [TestMethod]
    public async Task SyncAppliesRemoteOnlyChangesToLocalTodo()
    {
        var remoteTitle = "Initial title";
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/issues", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(ManagedIssue(53, remoteTitle, "Initial body")));
            }

            Assert.Fail($"Unexpected GitHub API request: {request.Method} {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });
        using var client = new HttpClient(handler);
        var sync = CreateService(client);

        await sync.SyncAsync();
        remoteTitle = "Edited on GitHub";
        var result = await sync.SyncAsync();

        Assert.AreEqual(1, result.Imported);
        var imported = await _context.TodoItems.SingleAsync(item => item.RepoId == _repoId);
        Assert.AreEqual("Edited on GitHub", imported.Title);
    }

    [TestMethod]
    public async Task ClosingIssueCompletesTodoAndNormalizesItsStatusLabel()
    {
        var remoteState = "open";
        string? statusUpdate = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/issues", StringComparison.Ordinal))
            {
                var issue = new
                {
                    number = 55,
                    title = "Close to complete",
                    body = "",
                    state = remoteState,
                    labels = new[] { new { name = "repotodo" }, new { name = "repotodo-status-notstarted" } }
                };
                return JsonResponse(JsonSerializer.Serialize(new[] { issue }));
            }

            if (request.Method == HttpMethod.Get && path.Contains("/labels/", StringComparison.Ordinal))
            {
                var name = Uri.UnescapeDataString(path[(path.LastIndexOf("/labels/", StringComparison.Ordinal) + 8)..]);
                return JsonResponse(JsonSerializer.Serialize(new { name }));
            }

            if (request.Method == HttpMethod.Patch && path.EndsWith("/issues/55", StringComparison.Ordinal))
            {
                statusUpdate = await request.Content!.ReadAsStringAsync();
                return JsonResponse("{} ");
            }

            Assert.Fail($"Unexpected GitHub API request: {request.Method} {request.RequestUri}");
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        using var client = new HttpClient(handler);
        var sync = CreateService(client);

        await sync.SyncAsync();
        remoteState = "closed";
        var result = await sync.SyncAsync();

        Assert.AreEqual(TodoItemStatus.Completed, (await _context.TodoItems.SingleAsync()).Status);
        Assert.AreEqual(1, result.Imported);
        Assert.AreEqual(1, result.Published);
        Assert.IsNotNull(statusUpdate);
        using var update = JsonDocument.Parse(statusUpdate);
        Assert.AreEqual("closed", update.RootElement.GetProperty("state").GetString());
        CollectionAssert.Contains(
            update.RootElement.GetProperty("labels").EnumerateArray().Select(label => label.GetString()).ToArray(),
            "repotodo-status-completed");
    }

    [TestMethod]
    public async Task SyncImportsAndUpdatesSubtasksFromIssueChecklist()
    {
        var issueTitle = "Parent task";
        var issueBody = "Parent details\n\n<!-- repotodo:subtasks -->\n- [ ] First step\n- [x] Second step\n";
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/issues", StringComparison.Ordinal))
            {
                var issue = new
                {
                    number = 54,
                    title = issueTitle,
                    body = issueBody,
                    state = "open",
                    labels = new[] { new { name = "repotodo" }, new { name = "repotodo-status-onhold" } }
                };
                return Task.FromResult(JsonResponse(JsonSerializer.Serialize(new[] { issue })));
            }

            Assert.Fail($"Unexpected GitHub API request: {request.Method} {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });
        using var client = new HttpClient(handler);
        var sync = CreateService(client);

        await sync.SyncAsync();
        var item = await _context.TodoItems.Include(todo => todo.Subtasks).SingleAsync();
        Assert.AreEqual("Parent details", item.Body);
        Assert.AreEqual(TodoItemStatus.OnHold, item.Status);
        Assert.AreEqual(TodoItemStatus.NotStarted, item.Subtasks.First(subtask => subtask.Title == "First step").Status);
        Assert.AreEqual(TodoItemStatus.Completed, item.Subtasks.First(subtask => subtask.Title == "Second step").Status);

        issueTitle = "Updated parent task";
        issueBody = "Edited details\n\n<!-- repotodo:subtasks -->\n- [x] First step\n- [ ] Second step\n";
        var result = await sync.SyncAsync();

        Assert.AreEqual(1, result.Imported);
        var updated = await _context.TodoItems.Include(todo => todo.Subtasks).SingleAsync();
        Assert.AreEqual("Updated parent task", updated.Title);
        Assert.AreEqual("Edited details", updated.Body);
        Assert.AreEqual(TodoItemStatus.Completed, updated.Subtasks.First(subtask => subtask.Title == "First step").Status);
        Assert.AreEqual(TodoItemStatus.NotStarted, updated.Subtasks.First(subtask => subtask.Title == "Second step").Status);
    }

    [TestMethod]
    public async Task DeletingLinkedTodoClosesAndUnlinksItsIssue()
    {
        var item = new TodoItem
        {
            RepoId = _repoId,
            Title = "Remove me",
            Body = "",
            Status = TodoItemStatus.NotStarted
        };
        _context.TodoItems.Add(item);
        await _context.SaveChangesAsync();
        _context.TodoGitHubSyncStates.Add(new TodoGitHubSyncState
        {
            RepoId = _repoId,
            TodoItemId = item.TodoItemId,
            GitHubIssueNumber = 64,
            LastSyncedSnapshot = "{}"
        });
        await _context.SaveChangesAsync();
        await new TodoService(_context, _repoId).DeleteTodoItem(item.TodoItemId);
        Assert.IsTrue((await _context.TodoGitHubSyncStates.SingleAsync()).IsDeleted);

        string? updatedIssue = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/issues", StringComparison.Ordinal))
            {
                return JsonResponse(ManagedIssue(64, "Remove me", "", "repotodo-status-notstarted", "repotodo-tag-old", "triage"));
            }

            if (request.Method == HttpMethod.Patch && request.RequestUri!.AbsolutePath.EndsWith("/issues/64", StringComparison.Ordinal))
            {
                updatedIssue = await request.Content!.ReadAsStringAsync();
                return JsonResponse("{} ");
            }

            Assert.Fail($"Unexpected GitHub API request: {request.Method} {request.RequestUri}");
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        using var client = new HttpClient(handler);
        var result = await CreateService(client).SyncAsync();

        Assert.AreEqual(1, result.Unlinked);
        Assert.IsNotNull(updatedIssue);
        using var update = JsonDocument.Parse(updatedIssue);
        Assert.AreEqual("closed", update.RootElement.GetProperty("state").GetString());
        var labels = update.RootElement.GetProperty("labels").EnumerateArray().Select(label => label.GetString()).ToArray();
        CollectionAssert.AreEqual(new[] { "triage" }, labels);
        Assert.IsEmpty(await _context.TodoItems.ToListAsync());
        Assert.IsTrue((await _context.TodoGitHubSyncStates.SingleAsync()).IsDeleted);
    }

    [TestMethod]
    public async Task SyncRequiresGitHubRemoteAndTokenWithoutMakingRequests()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));
        var missingRemote = new GitHubIssueSyncService(_context, _repoId, null, "token", client);
        await Assert.ThrowsAsync<InvalidOperationException>(() => missingRemote.SyncAsync());

        var missingToken = new GitHubIssueSyncService(_context, _repoId, new GitHubRepository("owner", "repo"), null, client);
        await Assert.ThrowsAsync<InvalidOperationException>(() => missingToken.SyncAsync());
    }

    private GitHubIssueSyncService CreateService(HttpClient client)
    {
        return new GitHubIssueSyncService(_context, _repoId, new GitHubRepository("owner", "repo"), "test-token", client);
    }

    private static (string Name, string Url) Remote(string name, string url)
    {
        return (name, url);
    }

    private static string ManagedIssue(int number, string title, string body, params string[] labels)
    {
        var issue = new
        {
            number,
            title,
            body,
            state = "open",
            labels = new[] { "repotodo", "repotodo-status-notstarted" }
                .Concat(labels)
                .Distinct()
                .Select(name => new { name })
                .ToArray()
        };
        return JsonSerializer.Serialize(new[] { issue });
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpRequestMessage request)
    {
        using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return send(request);
        }
    }
}
