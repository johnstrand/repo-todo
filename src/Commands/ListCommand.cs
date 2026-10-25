using System.Text.Json;
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

    [CommandOption("-j|--json")]
    [Description("Output the list as JSON.")]
    public bool Json { get; set; }
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

        if (settings.Json)
        {
            var items = todoItems.Select(t => new
            {
                id = t.TodoItemId,
                title = t.Title,
                status = t.Status.ToString(),
                tags = t.Tags,
                subtasks = t.Subtasks.Select(s => new
                {
                    id = s.TodoItemSubtaskId,
                    title = s.Title,
                    status = s.Status.ToString()
                })
            });

            Console.WriteLine(JsonSerializer.Serialize(items));

            return 0;
        }

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

            if (todoItem.Status == TodoItemStatus.Completed)
            {
                continue;
            }

            foreach (var subtask in todoItem.Subtasks.Where(s => s.Status != TodoItemStatus.Completed))
            {
                Console.WriteLine($"    - subtask #{subtask.TodoItemSubtaskId} of todo #{todoItem.TodoItemId}: {subtask.Title} [{subtask.Status}]");
            }
        }

        return 0;
    }
}