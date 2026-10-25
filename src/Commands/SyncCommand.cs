using RepoTodo.Services;
using Spectre.Console.Cli;

namespace RepoTodo.Commands;

internal sealed class SyncCommandSettings : CommandSettings
{
}

internal sealed class SyncCommand(GitHubIssueSyncService syncService) : AsyncCommand<SyncCommandSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        SyncCommandSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await syncService.SyncAsync(cancellationToken);
            Console.WriteLine(
                $"GitHub sync complete: imported {result.Imported}, published {result.Published}, unlinked {result.Unlinked}, conflicts {result.Conflicts.Count}.");

            foreach (var conflict in result.Conflicts)
            {
                Console.WriteLine($"Conflict for todo #{conflict.TodoItemId} / issue #{conflict.IssueNumber}: {conflict.Message}");
            }

            return result.Conflicts.Count == 0 ? 0 : 1;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidOperationException or InvalidDataException or IOException)
        {
            Console.WriteLine($"Could not sync with GitHub: {exception.Message}");
            return 1;
        }
    }
}
