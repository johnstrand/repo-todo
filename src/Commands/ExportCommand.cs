using System.ComponentModel;
using RepoTodo.Services;
using Spectre.Console.Cli;

namespace RepoTodo.Commands;

internal sealed class ExportCommandSettings : CommandSettings
{
    [CommandArgument(0, "<file>")]
    public required string FilePath { get; set; }

    [CommandOption("--force")]
    [Description("Overwrite the export file if it already exists.")]
    public bool Force { get; set; }
}

internal sealed class ExportCommand(TodoTransferService transferService) : AsyncCommand<ExportCommandSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        ExportCommandSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.Force && File.Exists(settings.FilePath))
        {
            Console.WriteLine($"Export file already exists: {settings.FilePath}. Use --force to overwrite it.");
            return 1;
        }

        try
        {
            var mode = settings.Force ? FileMode.Create : FileMode.CreateNew;
            await using var destination = new FileStream(settings.FilePath, mode, FileAccess.Write, FileShare.None);
            var count = await transferService.ExportAsync(destination);
            Console.WriteLine($"Exported {count} todo item(s) to {settings.FilePath}.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not export todos to {settings.FilePath}: {exception.Message}");
            return 1;
        }
    }
}
