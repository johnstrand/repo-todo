using RepoTodo.Data;
using RepoTodo.Services;
using Spectre.Console.Cli;

namespace RepoTodo.Commands;

internal class GetCommandSettings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    public required string Id { get; set; }
}

internal class GetCommand : AsyncCommand<GetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, GetCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!int.TryParse(settings.Id, out var todoItemId))
        {
            Console.WriteLine($"Invalid todo item id: {settings.Id}");

            return 1;
        }

        var todoItem = await TodoService.GetTodoItem(todoItemId);

        if (todoItem is null)
        {
            Console.WriteLine($"No todo item found with id {todoItemId}.");

            return 1;
        }

        Console.WriteLine($"#{todoItem.TodoItemId} [{todoItem.Status}]: {todoItem.Title}");

        if (!string.IsNullOrEmpty(todoItem.Body))
        {
            Console.WriteLine($"Body: {todoItem.Body}");
        }

        if (todoItem.Tags.Count > 0)
        {
            Console.WriteLine($"Tags: {string.Join(", ", todoItem.Tags.Select(tag => $"#{tag}"))}");
        }

        foreach (var subtask in todoItem.Subtasks.Where(s => s.Status != TodoItemStatus.Completed))
        {
            Console.WriteLine($"    - subtask #{subtask.TodoItemSubtaskId} of todo #{todoItem.TodoItemId}: {subtask.Title} [{subtask.Status}]");
        }

        return 0;
    }
}