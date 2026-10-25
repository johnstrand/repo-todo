using RepoTodo.Data;
using RepoTodo.Services;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace RepoTodo.Commands;

internal class CreateCommandSettings : CommandSettings
{
    [CommandArgument(0, "<title>")]
    public required string Title { get; set; }

    [CommandOption("-t|--tag <tag>")]
    [Description("Repeatable list of tags for the todo item.")]
    public string[] Tags { get; set; } = [];
}

internal class CreateCommand : AsyncCommand<CreateCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CreateCommandSettings settings, CancellationToken cancellationToken)
    {
        var newTodoItem = new TodoItem
        {
            Title = settings.Title,
            Status = TodoItemStatus.NotStarted,
            Tags = [.. settings.Tags],
            RemoteId = TodoService.RepoId
        };

        var todoItem = await TodoService.AddTodoItem(newTodoItem);

        Console.WriteLine($"Created todo item #{todoItem.TodoItemId}: {todoItem.Title}");

        return 0;
    }
}
