using RepoTodo.Data;
using RepoTodo.Services;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace RepoTodo.Commands;

internal class AddSubtaskCommandSettings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    public required string Id { get; set; }

    [CommandArgument(1, "<title>")]
    public required string Title { get; set; }
}

internal class AddSubtaskCommand : AsyncCommand<AddSubtaskCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, AddSubtaskCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!int.TryParse(settings.Id, out var parentTodoItemId))
        {
            Console.WriteLine($"Invalid todo item id: {settings.Id}");

            return 1;
        }

        var parentTodoItem = await TodoService.GetTodoItem(parentTodoItemId);

        if (parentTodoItem is null)
        {
            Console.WriteLine($"No todo item found with id {parentTodoItemId}.");

            return 1;
        }

        var subtask = await TodoService.AddSubtask(parentTodoItemId, settings.Title);

        if (subtask is null)
        {
            Console.WriteLine($"No todo item found with id {parentTodoItemId}.");

            return 1;
        }

        Console.WriteLine($"Added subtask #{subtask.TodoItemSubtaskId} to todo item #{parentTodoItemId}: {subtask.Title}");

        return 0;
    }
}