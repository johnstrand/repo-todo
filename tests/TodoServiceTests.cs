using System.Text.Json;
using LibGit2Sharp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RepoTodo.Commands;
using RepoTodo.Data;
using RepoTodo.Services;
using Spectre.Console.Cli;

namespace RepoTodo.Tests;

[TestClass]
public sealed class TodoServiceTests
{
    private SqliteConnection _connection = null!;
    private TodoContext _context = null!;
    private TodoService _firstRepository = null!;
    private TodoService _secondRepository = null!;
    private int _firstRepositoryId;
    private int _secondRepositoryId;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new TodoContext(options);
        await _context.Database.EnsureCreatedAsync();

        var repositories = new RepositoryService(_context);
        _firstRepositoryId = await repositories.EnsureRepoExists("first-working-tree", _ => false);
        _secondRepositoryId = await repositories.EnsureRepoExists("second-working-tree", _ => false);
        _firstRepository = new TodoService(_context, _firstRepositoryId);
        _secondRepository = new TodoService(_context, _secondRepositoryId);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [TestMethod]
    public async Task ListFiltersByStatusTextTagsAndStablePagination()
    {
        var release = await AddTodoAsync(_firstRepository, "Release Plan", "Review 100% rollback_plan", TodoItemStatus.NotStarted, "ops", "release");
        var followUp = await AddTodoAsync(_firstRepository, "release follow-up", "Deployment checklist", TodoItemStatus.InProgress, "maintenance");
        var completed = await AddTodoAsync(_firstRepository, "Old release plan", "Already shipped", TodoItemStatus.Completed, "done");
        await AddTodoAsync(_secondRepository, "Release plan from another repository", "Should not leak", TodoItemStatus.NotStarted, "ops");

        var combinedFilter = await _firstRepository.ListTodoItems(
            titleFilter: "RELEASE",
            bodyFilter: "rollback",
            tagFilters: ["missing", "ops"]);
        CollectionAssert.AreEqual(new[] { release.TodoItemId }, combinedFilter.Select(item => item.TodoItemId).ToArray());

        var escapedWildcardFilter = await _firstRepository.ListTodoItems(bodyFilter: "% rollback_plan");
        CollectionAssert.AreEqual(new[] { release.TodoItemId }, escapedWildcardFilter.Select(item => item.TodoItemId).ToArray());

        var defaultPage = await _firstRepository.ListTodoItems(skip: 1, take: 1);
        CollectionAssert.AreEqual(new[] { followUp.TodoItemId }, defaultPage.Select(item => item.TodoItemId).ToArray());

        var completedOnly = await _firstRepository.ListTodoItems(TodoItemStatus.Completed);
        CollectionAssert.AreEqual(new[] { completed.TodoItemId }, completedOnly.Select(item => item.TodoItemId).ToArray());
    }

    [TestMethod]
    public async Task TaskAndSubtaskMutationsAreRepositoryAndParentScoped()
    {
        var parent = await AddTodoAsync(_firstRepository, "Parent task");
        var otherParent = await AddTodoAsync(_firstRepository, "Other parent");
        var subtask = await _firstRepository.AddSubtask(parent.TodoItemId, "Check migrations");
        Assert.IsNotNull(subtask);

        Assert.IsNull(await _secondRepository.GetTodoItem(parent.TodoItemId));
        Assert.IsNull(await _secondRepository.GetSubtask(parent.TodoItemId, subtask.TodoItemSubtaskId));
        Assert.IsNull(await _secondRepository.AddSubtask(parent.TodoItemId, "Cross-repository write"));
        Assert.IsNull(await _firstRepository.GetSubtask(otherParent.TodoItemId, subtask.TodoItemSubtaskId));
        Assert.IsEmpty(await _secondRepository.ListSubtasks(parent.TodoItemId));

        await _secondRepository.UpdateTodoItem(parent.TodoItemId, title: "Must not change");
        await _secondRepository.UpdateTodoItemStatus(parent.TodoItemId, TodoItemStatus.Completed);
        await _secondRepository.DeleteTodoItem(parent.TodoItemId);
        Assert.IsNull(await _secondRepository.DeleteSubtask(parent.TodoItemId, subtask.TodoItemSubtaskId));

        await _firstRepository.UpdateTodoItem(parent.TodoItemId, title: "Updated parent", body: "Updated body");
        await _firstRepository.UpdateSubtaskTitle(parent.TodoItemId, subtask.TodoItemSubtaskId, "Review migration plan");
        await _firstRepository.UpdateSubtaskStatus(parent.TodoItemId, subtask.TodoItemSubtaskId, TodoItemStatus.Completed);

        var detail = await _firstRepository.GetTodoItemWithSubtasks(parent.TodoItemId);
        Assert.IsNotNull(detail);
        Assert.AreEqual("Updated parent", detail.Title);
        Assert.AreEqual("Updated body", detail.Body);
        Assert.AreEqual("Review migration plan", detail.Subtasks.Single().Title);
        Assert.AreEqual(TodoItemStatus.Completed, detail.Subtasks.Single().Status);
        Assert.IsEmpty(await _firstRepository.ListSubtasks(parent.TodoItemId));
        Assert.HasCount(1, await _firstRepository.ListSubtasks(parent.TodoItemId, TodoItemStatus.Completed));

        var deletedSubtask = await _firstRepository.DeleteSubtask(parent.TodoItemId, subtask.TodoItemSubtaskId);
        Assert.IsNotNull(deletedSubtask);
        await _firstRepository.UpdateTodoItemStatus(parent.TodoItemId, TodoItemStatus.Ready);
        Assert.AreEqual(TodoItemStatus.Ready, (await _firstRepository.GetTodoItem(parent.TodoItemId))!.Status);

        var childToCascade = await _firstRepository.AddSubtask(otherParent.TodoItemId, "Deleted with parent");
        Assert.IsNotNull(childToCascade);
        await _firstRepository.DeleteTodoItem(otherParent.TodoItemId);
        Assert.IsNull(await _firstRepository.GetTodoItem(otherParent.TodoItemId));
        Assert.IsEmpty(await _firstRepository.ListSubtasks(otherParent.TodoItemId));
    }

    [TestMethod]
    public async Task RepositoryIdentitySupportsUnbornRepositoriesAndMigratesLegacyTasks()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), "RepoTodo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repositoryPath);

        try
        {
            Repository.Init(repositoryPath);
            using var gitRepository = new Repository(repositoryPath);
            var identifier = RepositoryIdentity.GetRepositoryIdentifier(gitRepository);
            Assert.AreEqual(0, gitRepository.Commits.Count());

            var repositoryService = new RepositoryService(_context);
            var stableRepositoryId = await repositoryService.EnsureRepoExists(identifier, _ => false);
            var existingTask = await AddTodoAsync(new TodoService(_context, stableRepositoryId), "Created before initial commit");

            var legacyCommitSha = CreateCommit(gitRepository, repositoryPath, "first commit");
            Assert.AreEqual(identifier, RepositoryIdentity.GetRepositoryIdentifier(gitRepository));

            var legacyRepository = new Repo { Identifier = legacyCommitSha };
            _context.Repos.Add(legacyRepository);
            await _context.SaveChangesAsync();
            var legacyTask = new TodoItem
            {
                RepoId = legacyRepository.RepoId,
                Title = "Task stored against old commit identity",
                Body = "Preserve this task",
                Status = TodoItemStatus.NotStarted
            };
            _context.TodoItems.Add(legacyTask);

            var unrelatedRepository = new Repo { Identifier = "unrelated-legacy-identifier" };
            _context.Repos.Add(unrelatedRepository);
            await _context.SaveChangesAsync();
            var unrelatedTask = new TodoItem
            {
                RepoId = unrelatedRepository.RepoId,
                Title = "Keep unrelated data in place",
                Body = "Not part of this checkout",
                Status = TodoItemStatus.NotStarted
            };
            _context.TodoItems.Add(unrelatedTask);
            await _context.SaveChangesAsync();

            var migratedRepositoryId = await repositoryService.EnsureRepoExists(
                identifier,
                oldIdentifier => gitRepository.Lookup<Commit>(oldIdentifier) is not null);
            Assert.AreEqual(stableRepositoryId, migratedRepositoryId);
            Assert.AreEqual(stableRepositoryId, (await _context.TodoItems.SingleAsync(item => item.TodoItemId == legacyTask.TodoItemId)).RepoId);
            Assert.IsFalse(await _context.Repos.AnyAsync(repo => repo.Identifier == legacyCommitSha));
            Assert.AreEqual(unrelatedRepository.RepoId, (await _context.TodoItems.SingleAsync(item => item.TodoItemId == unrelatedTask.TodoItemId)).RepoId);
            Assert.IsTrue(await _context.Repos.AnyAsync(repo => repo.Identifier == unrelatedRepository.Identifier));
            Assert.IsNotNull(await new TodoService(_context, stableRepositoryId).GetTodoItem(existingTask.TodoItemId));

            CreateCommit(gitRepository, repositoryPath, "second commit");
            Assert.AreEqual(identifier, RepositoryIdentity.GetRepositoryIdentifier(gitRepository));
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [TestMethod]
    public async Task MigrationsCreateTheSchemaAndAllowServicePersistence()
    {
        await using var migrationConnection = new SqliteConnection("Data Source=:memory:");
        await migrationConnection.OpenAsync();

        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseSqlite(migrationConnection)
            .Options;
        await using var migrationContext = new TodoContext(options);

        await migrationContext.Database.MigrateAsync();
        Assert.IsEmpty(await migrationContext.Database.GetPendingMigrationsAsync());

        var repositoryId = await new RepositoryService(migrationContext)
            .EnsureRepoExists("migrated-working-tree", _ => false);
        var service = new TodoService(migrationContext, repositoryId);
        var item = await AddTodoAsync(service, "Created after migration", "Schema is ready", tags: ["migration"]);

        Assert.AreEqual(item.TodoItemId, (await service.GetTodoItem(item.TodoItemId))!.TodoItemId);
    }

    [TestMethod]
    public void InputValidationIsPureAndRejectsInvalidIdsStatusesAndPagination()
    {
        Assert.IsTrue(CommandInputValidation.TryParsePositiveId("42", out var id));
        Assert.AreEqual(42, id);
        Assert.IsFalse(CommandInputValidation.TryParsePositiveId("0", out _));
        Assert.IsFalse(CommandInputValidation.TryParsePositiveId("-2", out _));
        Assert.IsFalse(CommandInputValidation.TryParsePositiveId("not-an-id", out _));
        Assert.IsFalse(CommandInputValidation.TryParsePositiveId("2147483648", out _));

        Assert.IsTrue(CommandInputValidation.TryParseStatus("inprogress", out var status));
        Assert.AreEqual(TodoItemStatus.InProgress, status);
        Assert.IsFalse(CommandInputValidation.TryParseStatus("99", out _));
        Assert.IsFalse(CommandInputValidation.TryParseStatus("-1", out _));

        Assert.IsNull(CommandInputValidation.ValidatePagination(null, null));
        Assert.IsNull(CommandInputValidation.ValidatePagination(0, 0));
        Assert.AreEqual("--skip must be zero or greater.", CommandInputValidation.ValidatePagination(-1, null));
        Assert.AreEqual("--take must be zero or greater.", CommandInputValidation.ValidatePagination(null, -1));
    }

    [TestMethod]
    public async Task CommandsCanBeInvokedInProcessWithInjectedServices()
    {
        var helpResult = await CreateCommandApp(_firstRepository, _firstRepositoryId).RunAsync(new[] { "--help" });
        Assert.AreEqual(0, helpResult);

        var createResult = await CreateCommandApp(_firstRepository, _firstRepositoryId).RunAsync(
            new[] { "create", "Command-layer task", "--body", "Created without a child process", "--tag", "unit" });
        Assert.AreEqual(0, createResult);

        var task = (await _firstRepository.ListTodoItems(titleFilter: "Command-layer task")).Single();
        var addSubtaskResult = await CreateCommandApp(_firstRepository, _firstRepositoryId).RunAsync(
            new[] { "subtask", "add", task.TodoItemId.ToString(), "Command-layer subtask" });
        Assert.AreEqual(0, addSubtaskResult);

        var detail = await _firstRepository.GetTodoItemWithSubtasks(task.TodoItemId);
        Assert.IsNotNull(detail);
        Assert.AreEqual("Command-layer subtask", detail.Subtasks.Single().Title);

        var invalidIdResult = await CreateCommandApp(_firstRepository, _firstRepositoryId).RunAsync(new[] { "delete", "not-an-id" });
        Assert.AreNotEqual(0, invalidIdResult);
        var invalidStatusResult = await CreateCommandApp(_firstRepository, _firstRepositoryId).RunAsync(new[] { "list", "--status", "99" });
        Assert.AreNotEqual(0, invalidStatusResult);
        var invalidPaginationResult = await CreateCommandApp(_firstRepository, _firstRepositoryId).RunAsync(new[] { "list", "--skip", "-1" });
        Assert.AreNotEqual(0, invalidPaginationResult);
    }

    [TestMethod]
    public async Task ExportImportPreservesAllFieldsAndAddsFreshRepositoryLocalIds()
    {
        var openItem = await AddTodoAsync(_firstRepository, "Open task", "Body text", TodoItemStatus.InProgress, "release", "ops");
        var completedItem = await AddTodoAsync(_firstRepository, "Completed task", "Done", TodoItemStatus.Completed, "archive");
        var openSubtask = await _firstRepository.AddSubtask(openItem.TodoItemId, "Open subtask");
        var completedSubtask = await _firstRepository.AddSubtask(openItem.TodoItemId, "Completed subtask");
        Assert.IsNotNull(openSubtask);
        Assert.IsNotNull(completedSubtask);
        await _firstRepository.UpdateSubtaskStatus(openItem.TodoItemId, completedSubtask.TodoItemSubtaskId, TodoItemStatus.Completed);
        var existingTargetItem = await AddTodoAsync(_secondRepository, "Keep existing target item");

        var export = new MemoryStream();
        var exportedCount = await new TodoTransferService(_context, _firstRepositoryId).ExportAsync(export);
        Assert.AreEqual(2, exportedCount);

        using var json = JsonDocument.Parse(export.ToArray());
        Assert.AreEqual(1, json.RootElement.GetProperty("formatVersion").GetInt32());
        var exportedItems = json.RootElement.GetProperty("items");
        Assert.AreEqual(2, exportedItems.GetArrayLength());
        Assert.IsFalse(exportedItems[0].TryGetProperty("id", out _));
        Assert.AreEqual("Completed", exportedItems[1].GetProperty("status").GetString());
        Assert.AreEqual("Body text", exportedItems[0].GetProperty("body").GetString());
        Assert.AreEqual(2, exportedItems[0].GetProperty("tags").GetArrayLength());
        Assert.AreEqual(2, exportedItems[0].GetProperty("subtasks").GetArrayLength());

        var import = new TodoTransferService(_context, _secondRepositoryId);
        export.Position = 0;
        Assert.AreEqual(2, await import.ImportAsync(export));

        var importedItems = await _context.TodoItems
            .Where(item => item.RepoId == _secondRepositoryId)
            .Include(item => item.Subtasks)
            .OrderBy(item => item.TodoItemId)
            .ToListAsync();
        Assert.HasCount(3, importedItems);
        var importedOpenItem = importedItems.Single(item => item.Title == "Open task");
        var importedCompletedItem = importedItems.Single(item => item.Title == "Completed task");
        Assert.AreEqual("Body text", importedOpenItem.Body);
        Assert.AreEqual(TodoItemStatus.InProgress, importedOpenItem.Status);
        CollectionAssert.AreEqual(new[] { "release", "ops" }, importedOpenItem.Tags.ToArray());
        Assert.AreNotEqual(openItem.TodoItemId, importedOpenItem.TodoItemId);
        Assert.AreEqual(_secondRepositoryId, importedOpenItem.RepoId);
        Assert.AreEqual(TodoItemStatus.Completed, importedCompletedItem.Status);
        Assert.AreEqual(TodoItemStatus.Completed, importedOpenItem.Subtasks.Single(subtask => subtask.Title == "Completed subtask").Status);
        Assert.AreEqual(existingTargetItem.TodoItemId, importedItems[0].TodoItemId);

        export.Position = 0;
        Assert.AreEqual(2, await import.ImportAsync(export));
        Assert.HasCount(5, await _context.TodoItems.Where(item => item.RepoId == _secondRepositoryId).ToListAsync());
    }

    [TestMethod]
    public async Task ImportRejectsUnsupportedOrInvalidDocumentsWithoutPartialWrites()
    {
        var service = new TodoTransferService(_context, _secondRepositoryId);
        var unsupportedVersion = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            """{"formatVersion":2,"exportedAtUtc":"2026-01-01T00:00:00Z","items":[]}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(unsupportedVersion));

        var invalidSecondItem = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            """{"formatVersion":1,"exportedAtUtc":"2026-01-01T00:00:00Z","items":[{"title":"Valid first","body":"","status":"NotStarted","tags":[],"subtasks":[]},{"title":"Invalid status","body":"","status":"Unknown","tags":[],"subtasks":[]}] }"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(invalidSecondItem));

        Assert.IsEmpty(await _context.TodoItems.Where(item => item.RepoId == _secondRepositoryId).ToListAsync());

        var malformedJson = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{not json"));
        await Assert.ThrowsAsync<JsonException>(() => service.ImportAsync(malformedJson));
    }

    [TestMethod]
    public async Task ExportAndImportCommandsUseFilesAndProtectExistingExports()
    {
        var root = Path.Combine(Path.GetTempPath(), "RepoTodo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var exportPath = Path.Combine(root, "todos.json");

        try
        {
            Assert.AreEqual(0, await CreateCommandApp(_firstRepository, _firstRepositoryId)
                .RunAsync(new[] { "export", "--help" }));
            Assert.AreEqual(0, await CreateCommandApp(_secondRepository, _secondRepositoryId)
                .RunAsync(new[] { "import", "--help" }));

            var task = await AddTodoAsync(_firstRepository, "Exported task", status: TodoItemStatus.Completed);
            var subtask = await _firstRepository.AddSubtask(task.TodoItemId, "Exported subtask");
            Assert.IsNotNull(subtask);

            var exportResult = await CreateCommandApp(_firstRepository, _firstRepositoryId)
                .RunAsync(new[] { "export", exportPath });
            Assert.AreEqual(0, exportResult);
            Assert.IsTrue(File.Exists(exportPath));

            var existingFileResult = await CreateCommandApp(_firstRepository, _firstRepositoryId)
                .RunAsync(new[] { "export", exportPath });
            Assert.AreNotEqual(0, existingFileResult);

            var forceExportResult = await CreateCommandApp(_firstRepository, _firstRepositoryId)
                .RunAsync(new[] { "export", exportPath, "--force" });
            Assert.AreEqual(0, forceExportResult);

            var importResult = await CreateCommandApp(_secondRepository, _secondRepositoryId)
                .RunAsync(new[] { "import", exportPath });
            Assert.AreEqual(0, importResult);

            var importedTask = (await _secondRepository.ListTodoItems(TodoItemStatus.Completed)).Single();
            Assert.AreEqual(task.Title, importedTask.Title);
            Assert.AreEqual(subtask.Title, importedTask.Subtasks.Single().Title);

            var invalidImportResult = await CreateCommandApp(_secondRepository, _secondRepositoryId)
                .RunAsync(new[] { "import", Path.Combine(root, "missing.json") });
            Assert.AreNotEqual(0, invalidImportResult);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private CommandApp CreateCommandApp(TodoService service, int repositoryId)
    {
        return TodoCommandApp.Create(service, new TodoTransferService(_context, repositoryId));
    }

    private static Task<TodoItem> AddTodoAsync(
        TodoService service,
        string title,
        string body = "",
        TodoItemStatus status = TodoItemStatus.NotStarted,
        params string[] tags)
    {
        return service.AddTodoItem(new TodoItem
        {
            Title = title,
            Body = body,
            Status = status,
            Tags = tags.ToList()
        });
    }

    private static string CreateCommit(Repository repository, string repositoryPath, string message)
    {
        const string fileName = "test-commit.txt";
        File.AppendAllText(Path.Combine(repositoryPath, fileName), message);
        LibGit2Sharp.Commands.Stage(repository, fileName);
        var signature = new Signature("RepoTodo Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
        return repository.Commit(message, signature, signature).Sha;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directory, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}
