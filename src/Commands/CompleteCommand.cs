using RepoTodo.Data;
using RepoTodo.Services;
using Spectre.Console.Cli;

namespace RepoTodo.Commands;

internal class CompleteCommandSettings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    public required string Id { get; set; }
}

internal class CompleteCommand : TodoAsyncCommand<CompleteCommandSettings>
{
    public CompleteCommand(TodoService todoService) : base(todoService)
    {
    }

    public override async Task<int> ExecuteAsync(CommandContext context, CompleteCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!CommandInputValidation.TryParsePositiveId(settings.Id, out var todoItemId))
        {
            Console.WriteLine($"Invalid todo item id: {settings.Id}");
            return 1;
        }

        var todoItem = await TodoService.GetTodoItem(todoItemId);

        if (todoItem is not null)
        {
            await TodoService.UpdateTodoItemStatus(todoItemId, TodoItemStatus.Completed);

            Console.WriteLine($"Completed todo item #{todoItem.TodoItemId}: {todoItem.Title}");

            return 0;
        }

        Console.WriteLine($"No todo item found with id {todoItemId}.");
        return 1;
    }
}
