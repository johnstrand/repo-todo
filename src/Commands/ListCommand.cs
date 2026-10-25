using RepoTodo.Data;
using RepoTodo.Services;
using Spectre.Console.Cli;

namespace RepoTodo.Commands;

internal class ListCommand : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var todoItems = await TodoService.ListTodoItems();

        if (todoItems.Count == 0)
        {
            Console.WriteLine("No todo items found.");

            return 0;
        }

        foreach (var todoItem in todoItems)
        {
            var tags = todoItem.Tags.Count > 0
                ? $" [{string.Join(", ", todoItem.Tags.Select(tag => $"#{tag}"))}]"
                : string.Empty;

            Console.WriteLine($"#{todoItem.TodoItemId} [{todoItem.Status}]{tags}: {todoItem.Title}");
        }

        return 0;
    }
}