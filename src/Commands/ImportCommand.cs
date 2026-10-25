using System.ComponentModel;
using System.Text.Json;
using RepoTodo.Services;
using Spectre.Console.Cli;

namespace RepoTodo.Commands;

internal sealed class ImportCommandSettings : CommandSettings
{
    [CommandArgument(0, "<file>")]
    [Description("The JSON export file to import.")]
    public required string FilePath { get; set; }
}

internal sealed class ImportCommand(TodoTransferService transferService) : AsyncCommand<ImportCommandSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        ImportCommandSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = new FileStream(settings.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var count = await transferService.ImportAsync(source);
            Console.WriteLine($"Imported {count} todo item(s) from {settings.FilePath}.");
            return 0;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not import todos from {settings.FilePath}: {exception.Message}");
            return 1;
        }
    }
}
