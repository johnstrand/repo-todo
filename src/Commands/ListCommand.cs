using RepoTodo.Data;
using RepoTodo.Services;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace RepoTodo.Commands;

internal class ListCommandSettings : CommandSettings
{
    [CommandOption("-s|--status <status>")]
    [Description("Filter the list by status.")]
    public string? Status { get; set; }
}

internal class ListCommand : AsyncCommand<ListCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ListCommandSettings settings, CancellationToken cancellationToken)
    {
        TodoItemStatus? statusFilter = null;

        if (settings.Status is not null)
        {
            if (!Enum.TryParse<TodoItemStatus>(settings.Status, ignoreCase: true, out var parsedStatus))
            {
                Console.WriteLine($"Unknown status: {settings.Status}");

                return 1;
            }

            statusFilter = parsedStatus;
        }

        var todoItems = await TodoService.ListTodoItems(statusFilter);

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