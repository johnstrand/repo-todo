using RepoTodo.Data;
using RepoTodo.Services;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace RepoTodo.Commands;

internal class UpdateCommandSettings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    public required string Id { get; set; }

    [CommandOption("--title <title>")]
    [Description("The new title of the todo item.")]
    public string? Title { get; set; }

    [CommandOption("--body <body>")]
    [Description("The new body text of the todo item.")]
    public string? Body { get; set; }
}

internal class UpdateCommand : AsyncCommand<UpdateCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UpdateCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!CommandInputValidation.TryParsePositiveId(settings.Id, "todo item id", out var todoItemId))
        {
            return 1;
        }

        var todoItem = await TodoService.GetTodoItem(todoItemId);

        if (todoItem is null)
        {
            Console.WriteLine($"No todo item found with id {todoItemId}.");

            return 1;
        }

        if (settings.Title is null && settings.Body is null)
        {
            Console.WriteLine("Nothing to update. Provide --title and/or --body.");

            return 1;
        }

        await TodoService.UpdateTodoItem(todoItemId, settings.Title, settings.Body);

        Console.WriteLine($"Updated todo item #{todoItemId}: {todoItem.Title}");

        return 0;
    }
}
