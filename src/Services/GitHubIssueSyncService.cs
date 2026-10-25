using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RepoTodo.Data;

namespace RepoTodo.Services;

internal sealed class GitHubIssueSyncService(
    TodoContext context,
    int repoId,
    GitHubRepository? repository,
    string? token,
    HttpClient httpClient)
{
    private const string ManagedLabel = "repotodo";
    private const string StatusLabelPrefix = "repotodo-status-";
    private const string TagLabelPrefix = "repotodo-tag-";
    private const string SubtasksMarker = "<!-- repotodo:subtasks -->";
    private const string MetadataPrefix = "<!-- repotodo:data:v1:";
    private const string MetadataSuffix = "-->";
    private const string ApiBase = "https://api.github.com/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GitHubSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (repository is null)
        {
            throw new InvalidOperationException("The current Git repository has no GitHub remote.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("TODO_GITHUB_TOKEN is not set.");
        }

        var result = new GitHubSyncResult();
        var issues = await ListManagedIssuesAsync(cancellationToken);
        var issuesByNumber = issues.ToDictionary(issue => issue.Number);
        var states = await context.TodoGitHubSyncStates
            .Where(state => state.RepoId == repoId)
            .ToListAsync(cancellationToken);
        var statesByIssue = states.ToDictionary(state => state.GitHubIssueNumber);

        foreach (var state in states.Where(state => state.IsDeleted))
        {
            if (issuesByNumber.TryGetValue(state.GitHubIssueNumber, out var deletedIssue))
            {
                await CloseAndUnlinkIssueAsync(deletedIssue, cancellationToken);
                result.Unlinked++;
            }
        }

        foreach (var issue in issues)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (statesByIssue.ContainsKey(issue.Number))
            {
                continue;
            }

            var snapshot = SnapshotFromIssue(issue);
            var item = new TodoItem
            {
                RepoId = repoId,
                Title = snapshot.Title,
                Body = snapshot.Body,
                Status = snapshot.Status,
                Tags = snapshot.Tags.ToList(),
                Subtasks = snapshot.Subtasks.Select(subtask => new TodoItemSubtask
                {
                    Title = subtask.Title,
                    Status = subtask.Status
                }).ToList()
            };

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            context.TodoItems.Add(item);
            await context.SaveChangesAsync(cancellationToken);

            var state = new TodoGitHubSyncState
            {
                RepoId = repoId,
                TodoItemId = item.TodoItemId,
                GitHubIssueNumber = issue.Number,
                LastSyncedSnapshot = SerializeSnapshot(snapshot)
            };
            context.TodoGitHubSyncStates.Add(state);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            states.Add(state);
            statesByIssue.Add(issue.Number, state);
            result.Imported++;
        }

        var localItems = await context.TodoItems
            .Where(item => item.RepoId == repoId)
            .Include(item => item.Subtasks)
            .OrderBy(item => item.TodoItemId)
            .ToListAsync(cancellationToken);
        var stateByTodoId = states
            .Where(state => !state.IsDeleted && state.TodoItemId.HasValue)
            .ToDictionary(state => state.TodoItemId!.Value);

        foreach (var item in localItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localSnapshot = SnapshotFromItem(item);

            if (!stateByTodoId.TryGetValue(item.TodoItemId, out var state))
            {
                var createdIssue = await CreateIssueAsync(localSnapshot, cancellationToken);
                state = new TodoGitHubSyncState
                {
                    RepoId = repoId,
                    TodoItemId = item.TodoItemId,
                    GitHubIssueNumber = createdIssue.Number,
                    LastSyncedSnapshot = SerializeSnapshot(localSnapshot)
                };
                context.TodoGitHubSyncStates.Add(state);
                await context.SaveChangesAsync(cancellationToken);
                states.Add(state);
                statesByIssue.Add(createdIssue.Number, state);
                result.Published++;
                continue;
            }

            if (!issuesByNumber.TryGetValue(state.GitHubIssueNumber, out var remoteIssue))
            {
                result.Conflicts.Add(new GitHubSyncConflict(
                    item.TodoItemId,
                    state.GitHubIssueNumber,
                    "The linked issue is missing or no longer has the RepoTodo marker."));
                continue;
            }

            var baseline = DeserializeSnapshot(state.LastSyncedSnapshot);
            var remoteSnapshot = SnapshotFromIssue(remoteIssue);
            var localChanged = !SnapshotsEqual(localSnapshot, baseline);
            var remoteChanged = !SnapshotsEqual(remoteSnapshot, baseline);

            if (localChanged && remoteChanged && !SnapshotsEqual(localSnapshot, remoteSnapshot))
            {
                result.Conflicts.Add(new GitHubSyncConflict(
                    item.TodoItemId,
                    state.GitHubIssueNumber,
                    "Both the local todo and GitHub issue changed since the last sync."));
                continue;
            }

            if (localChanged && !remoteChanged)
            {
                await UpdateIssueAsync(remoteIssue, localSnapshot, cancellationToken);
                state.LastSyncedSnapshot = SerializeSnapshot(localSnapshot);
                result.Published++;
            }
            else if (remoteChanged && !localChanged)
            {
                ApplySnapshot(item, remoteSnapshot);
                if (!ManagedFieldsMatch(remoteIssue, remoteSnapshot))
                {
                    await UpdateManagedIssueFieldsAsync(remoteIssue, remoteSnapshot, cancellationToken);
                    result.Published++;
                }

                state.LastSyncedSnapshot = SerializeSnapshot(remoteSnapshot);
                result.Imported++;
            }
            else if (localChanged || remoteChanged)
            {
                state.LastSyncedSnapshot = SerializeSnapshot(localSnapshot);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<List<GitHubIssue>> ListManagedIssuesAsync(CancellationToken cancellationToken)
    {
        var issues = new List<GitHubIssue>();
        for (var page = 1; ; page++)
        {
            var batch = await SendAsync<List<GitHubIssue>>(
                HttpMethod.Get,
                $"issues?state=all&per_page=100&page={page}",
                content: null,
                cancellationToken);

            if (batch is null || batch.Count == 0)
            {
                break;
            }

            issues.AddRange(batch.Where(issue =>
                issue.PullRequest is null &&
                issue.Labels.Any(label => string.Equals(label.Name, ManagedLabel, StringComparison.OrdinalIgnoreCase))));

            if (batch.Count < 100)
            {
                break;
            }
        }

        return issues;
    }

    private async Task<GitHubIssue> CreateIssueAsync(TodoSyncSnapshot snapshot, CancellationToken cancellationToken)
    {
        var labels = ManagedLabels(snapshot);
        await EnsureLabelsExistAsync(labels, cancellationToken);
        var issue = await SendAsync<GitHubIssue>(
            HttpMethod.Post,
            "issues",
            new { title = snapshot.Title, body = CreateIssueBody(snapshot), labels },
            cancellationToken);
        if (issue is null)
        {
            throw new InvalidOperationException("GitHub returned an empty issue response.");
        }

        if (snapshot.Status == TodoItemStatus.Completed)
        {
            await SendAsync<GitHubIssue>(
                HttpMethod.Patch,
                $"issues/{issue.Number}",
                new { state = "closed" },
                cancellationToken);
        }

        return issue;
    }

    private async Task UpdateIssueAsync(
        GitHubIssue issue,
        TodoSyncSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var labels = issue.Labels
            .Select(label => label.Name)
            .Where(label => !IsManagedLabel(label))
            .Concat(ManagedLabels(snapshot))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        await EnsureLabelsExistAsync(ManagedLabels(snapshot), cancellationToken);
        await SendAsync<GitHubIssue>(
            HttpMethod.Patch,
            $"issues/{issue.Number}",
            new { title = snapshot.Title, body = CreateIssueBody(snapshot), labels, state = IssueState(snapshot.Status) },
            cancellationToken);
    }

    private async Task CloseAndUnlinkIssueAsync(GitHubIssue issue, CancellationToken cancellationToken)
    {
        var labels = issue.Labels
            .Select(label => label.Name)
            .Where(label => !IsManagedLabel(label))
            .ToList();
        await SendAsync<GitHubIssue>(
            HttpMethod.Patch,
            $"issues/{issue.Number}",
            new { state = "closed", labels },
            cancellationToken);
    }

    private async Task UpdateManagedIssueFieldsAsync(
        GitHubIssue issue,
        TodoSyncSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var managedLabels = ManagedLabels(snapshot);
        var labels = issue.Labels
            .Select(label => label.Name)
            .Where(label => !IsManagedLabel(label))
            .Concat(managedLabels)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        await EnsureLabelsExistAsync(managedLabels, cancellationToken);
        await SendAsync<GitHubIssue>(
            HttpMethod.Patch,
            $"issues/{issue.Number}",
            new { state = IssueState(snapshot.Status), labels },
            cancellationToken);
    }

    private async Task EnsureLabelsExistAsync(IEnumerable<string> labels, CancellationToken cancellationToken)
    {
        foreach (var label in labels.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var encodedLabel = Uri.EscapeDataString(label);
            var existing = await SendAsync<GitHubLabel>(
                HttpMethod.Get,
                $"labels/{encodedLabel}",
                content: null,
                cancellationToken,
                allowNotFound: true);
            if (existing is not null)
            {
                continue;
            }

            await SendAsync<GitHubLabel>(
                HttpMethod.Post,
                "labels",
                new { name = label, color = LabelColor(label), description = "Managed by RepoTodo" },
                cancellationToken);
        }
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? content,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(ApiBase), RepositoryPath(path)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("RepoTodo/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (content is not null)
        {
            request.Content = JsonContent.Create(content, options: JsonOptions);
        }

        using (request)
        using (var response = await httpClient.SendAsync(request, cancellationToken))
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"GitHub API request failed with {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            if (response.Content.Headers.ContentLength == 0 || response.StatusCode == HttpStatusCode.NoContent)
            {
                return default;
            }

            return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
        }
    }

    private string RepositoryPath(string path)
    {
        return $"repos/{Uri.EscapeDataString(repository!.Owner)}/{Uri.EscapeDataString(repository.Name)}/{path}";
    }

    private static TodoSyncSnapshot SnapshotFromItem(TodoItem item)
    {
        return new TodoSyncSnapshot
        {
            Title = item.Title,
            Body = item.Body,
            Status = item.Status,
            Tags = item.Tags.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList(),
            Subtasks = item.Subtasks
                .OrderBy(subtask => subtask.TodoItemSubtaskId)
                .Select(subtask => new TodoSyncSubtask { Title = subtask.Title, Status = subtask.Status })
                .ToList()
        };
    }

    private static TodoSyncSnapshot SnapshotFromIssue(GitHubIssue issue)
    {
        var labels = issue.Labels.Select(label => label.Name).ToList();
        var data = ReadIssueMetadata(issue.Body);
        var tags = labels
            .Where(label => label.StartsWith(TagLabelPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(label => label[TagLabelPrefix.Length..])
            .ToList();

        if (data is not null)
        {
            foreach (var tag in data.Tags.Where(tag => TagLabel(tag) is null))
            {
                tags.Add(tag);
            }
        }

        var status = issue.State.Equals("closed", StringComparison.OrdinalIgnoreCase)
            ? TodoItemStatus.Completed
            : labels
                .Where(label => label.StartsWith(StatusLabelPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(label => ParseStatusLabel(label[StatusLabelPrefix.Length..]))
                .FirstOrDefault(TodoItemStatus.NotStarted);

        var displayedSubtasks = ReadDisplayedSubtasks(issue.Body);
        var subtasks = displayedSubtasks is null
            ? data?.Subtasks ?? []
            : MapDisplayedSubtasks(displayedSubtasks, data?.Subtasks);

        return new TodoSyncSnapshot
        {
            Title = issue.Title,
            Body = ReadUserBody(issue.Body),
            Status = status,
            Tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList(),
            Subtasks = subtasks
        };
    }

    private static void ApplySnapshot(TodoItem item, TodoSyncSnapshot snapshot)
    {
        item.Title = snapshot.Title;
        item.Body = snapshot.Body;
        item.Status = snapshot.Status;
        item.Tags = snapshot.Tags.ToList();
        item.Subtasks.Clear();
        foreach (var subtask in snapshot.Subtasks)
        {
            item.Subtasks.Add(new TodoItemSubtask
            {
                TodoItemId = item.TodoItemId,
                Title = subtask.Title,
                Status = subtask.Status
            });
        }
    }

    private static string CreateIssueBody(TodoSyncSnapshot snapshot)
    {
        var body = new StringBuilder(snapshot.Body);
        if (body.Length > 0)
        {
            body.AppendLine().AppendLine();
        }

        body.Append(SubtasksMarker).AppendLine();
        foreach (var subtask in snapshot.Subtasks)
        {
            body.Append(subtask.Status == TodoItemStatus.Completed ? "- [x] " : "- [ ] ")
                .AppendLine(subtask.Title.Replace("\r", " ").Replace("\n", " "));
        }

        var metadata = JsonSerializer.Serialize(new TodoIssueMetadata { Tags = snapshot.Tags, Subtasks = snapshot.Subtasks }, JsonOptions);
        var encodedMetadata = Convert.ToBase64String(Encoding.UTF8.GetBytes(metadata));
        body.Append(MetadataPrefix).Append(encodedMetadata).Append(MetadataSuffix);
        return body.ToString();
    }

    private static string ReadUserBody(string? issueBody)
    {
        if (string.IsNullOrEmpty(issueBody))
        {
            return "";
        }

        var marker = issueBody.LastIndexOf(SubtasksMarker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return issueBody;
        }

        var body = issueBody[..marker];
        if (body.EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            return body[..^4];
        }

        return body.EndsWith("\n\n", StringComparison.Ordinal) ? body[..^2] : body.TrimEnd('\r', '\n');
    }

    private static TodoIssueMetadata? ReadIssueMetadata(string? issueBody)
    {
        if (string.IsNullOrEmpty(issueBody))
        {
            return null;
        }

        var marker = issueBody.LastIndexOf(MetadataPrefix, StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        var start = marker + MetadataPrefix.Length;
        var end = issueBody.IndexOf(MetadataSuffix, start, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(issueBody[start..end]));
            var metadata = JsonSerializer.Deserialize<TodoIssueMetadata>(json, JsonOptions);
            if (metadata is null || metadata.Tags is null || metadata.Subtasks is null ||
                metadata.Tags.Any(tag => tag is null) ||
                metadata.Subtasks.Any(subtask => subtask is null || !Enum.IsDefined(subtask.Status)))
            {
                return null;
            }

            return metadata;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<DisplayedSubtask>? ReadDisplayedSubtasks(string? issueBody)
    {
        if (string.IsNullOrEmpty(issueBody))
        {
            return null;
        }

        var marker = issueBody.LastIndexOf(SubtasksMarker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        var metadataMarker = issueBody.LastIndexOf(MetadataPrefix, StringComparison.Ordinal);
        var end = metadataMarker > marker ? metadataMarker : issueBody.Length;
        return issueBody[(marker + SubtasksMarker.Length)..end]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("- [ ] ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
            .Select(line => new DisplayedSubtask(line[6..], line[3] is 'x' or 'X'))
            .ToList();
    }

    private static List<TodoSyncSubtask> MapDisplayedSubtasks(
        List<DisplayedSubtask> displayedSubtasks,
        List<TodoSyncSubtask>? metadataSubtasks)
    {
        var mapped = new List<TodoSyncSubtask>(displayedSubtasks.Count);
        var usedMetadataIndexes = new HashSet<int>();
        var allowPositionFallback = metadataSubtasks is not null && displayedSubtasks.Count == metadataSubtasks.Count;

        for (var index = 0; index < displayedSubtasks.Count; index++)
        {
            var displayed = displayedSubtasks[index];
            var metadataIndex = -1;
            if (metadataSubtasks is not null)
            {
                for (var candidate = 0; candidate < metadataSubtasks.Count; candidate++)
                {
                    if (!usedMetadataIndexes.Contains(candidate) &&
                        string.Equals(DisplaySubtaskTitle(metadataSubtasks[candidate].Title), displayed.Title, StringComparison.Ordinal))
                    {
                        metadataIndex = candidate;
                        break;
                    }
                }
            }

            if (metadataIndex < 0 && allowPositionFallback && index < metadataSubtasks!.Count && !usedMetadataIndexes.Contains(index))
            {
                metadataIndex = index;
            }

            TodoItemStatus status;
            if (metadataIndex >= 0)
            {
                usedMetadataIndexes.Add(metadataIndex);
                var previousStatus = metadataSubtasks![metadataIndex].Status;
                status = (previousStatus == TodoItemStatus.Completed) == displayed.IsCompleted
                    ? previousStatus
                    : displayed.IsCompleted ? TodoItemStatus.Completed : TodoItemStatus.NotStarted;
            }
            else
            {
                status = displayed.IsCompleted ? TodoItemStatus.Completed : TodoItemStatus.NotStarted;
            }

            var title = metadataIndex >= 0 &&
                string.Equals(DisplaySubtaskTitle(metadataSubtasks![metadataIndex].Title), displayed.Title, StringComparison.Ordinal)
                    ? metadataSubtasks[metadataIndex].Title
                    : displayed.Title;
            mapped.Add(new TodoSyncSubtask { Title = title, Status = status });
        }

        return mapped;
    }

    private static List<string> ManagedLabels(TodoSyncSnapshot snapshot)
    {
        var labels = new List<string> { ManagedLabel, StatusLabel(snapshot.Status) };
        labels.AddRange(snapshot.Tags.Select(TagLabel).Where(label => label is not null).Select(label => label!));
        return labels;
    }

    private static string StatusLabel(TodoItemStatus status)
    {
        return $"{StatusLabelPrefix}{status.ToString().ToLowerInvariant()}";
    }

    private static string? TagLabel(string tag)
    {
        var label = $"{TagLabelPrefix}{tag}";
        return label.Length <= 50 ? label : null;
    }

    private static bool IsManagedLabel(string label)
    {
        return string.Equals(label, ManagedLabel, StringComparison.OrdinalIgnoreCase) ||
            label.StartsWith(StatusLabelPrefix, StringComparison.OrdinalIgnoreCase) ||
            label.StartsWith(TagLabelPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplaySubtaskTitle(string title)
    {
        return title.Replace("\r", " ").Replace("\n", " ");
    }

    private static bool ManagedFieldsMatch(GitHubIssue issue, TodoSyncSnapshot snapshot)
    {
        var currentLabels = issue.Labels
            .Select(label => label.Name)
            .Where(IsManagedLabel)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedLabels = ManagedLabels(snapshot).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return string.Equals(issue.State, IssueState(snapshot.Status), StringComparison.OrdinalIgnoreCase) &&
            currentLabels.SetEquals(expectedLabels);
    }

    private static TodoItemStatus ParseStatusLabel(string value)
    {
        var normalized = value.Replace("-", "", StringComparison.Ordinal);
        return Enum.TryParse<TodoItemStatus>(normalized, ignoreCase: true, out var status) && Enum.IsDefined(status)
            ? status
            : TodoItemStatus.NotStarted;
    }

    private static string IssueState(TodoItemStatus status)
    {
        return status == TodoItemStatus.Completed ? "closed" : "open";
    }

    private static string LabelColor(string label)
    {
        return label == ManagedLabel ? "1f6feb" : label.StartsWith(StatusLabelPrefix, StringComparison.Ordinal) ? "8250df" : "0e8a16";
    }

    private static string SerializeSnapshot(TodoSyncSnapshot snapshot)
    {
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private static TodoSyncSnapshot DeserializeSnapshot(string value)
    {
        return JsonSerializer.Deserialize<TodoSyncSnapshot>(value, JsonOptions)
            ?? throw new InvalidDataException("A stored GitHub sync snapshot is invalid.");
    }

    private static bool SnapshotsEqual(TodoSyncSnapshot left, TodoSyncSnapshot right)
    {
        return string.Equals(SerializeSnapshot(left), SerializeSnapshot(right), StringComparison.Ordinal);
    }

    private sealed class GitHubIssue
    {
        [JsonPropertyName("number")]
        public int Number { get; init; }

        [JsonPropertyName("title")]
        public string Title { get; init; } = "";

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("state")]
        public string State { get; init; } = "open";

        [JsonPropertyName("labels")]
        public List<GitHubLabel> Labels { get; init; } = [];

        [JsonPropertyName("pull_request")]
        public JsonElement? PullRequest { get; init; }
    }

    private sealed class GitHubLabel
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";
    }

    private sealed class TodoIssueMetadata
    {
        public List<string> Tags { get; init; } = [];
        public List<TodoSyncSubtask> Subtasks { get; init; } = [];
    }

    private sealed record DisplayedSubtask(string Title, bool IsCompleted);
}

internal sealed class TodoSyncSnapshot
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required TodoItemStatus Status { get; init; }
    public required List<string> Tags { get; init; }
    public required List<TodoSyncSubtask> Subtasks { get; init; }
}

internal sealed class TodoSyncSubtask
{
    public required string Title { get; init; }
    public required TodoItemStatus Status { get; init; }
}

internal sealed record GitHubSyncConflict(int TodoItemId, int IssueNumber, string Message);

internal sealed class GitHubSyncResult
{
    public int Imported { get; set; }
    public int Published { get; set; }
    public int Unlinked { get; set; }
    public List<GitHubSyncConflict> Conflicts { get; } = [];
}
