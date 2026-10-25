using RepoTodo.Data;
using RepoTodo.Services;
using Spectre.Console.Cli;

namespace RepoTodo.Commands;

internal class DeleteCommandSettings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    public required string Id { get; set; }
}

internal class DeleteCommand : AsyncCommand<DeleteCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DeleteCommandSettings settings, CancellationToken cancellationToken)
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

        await TodoService.DeleteTodoItem(todoItemId);

        Console.WriteLine($"Deleted todo item #{todoItem.TodoItemId}: {todoItem.Title}");

        return 0;
    }
}
