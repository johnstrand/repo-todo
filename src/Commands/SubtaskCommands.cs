using System.ComponentModel;
using System.Text.Json;
using RepoTodo.Data;
using RepoTodo.Services;
using Spectre.Console.Cli;

namespace RepoTodo.Commands;

internal static class SubtaskCommandHelpers
{
    public static bool TryParseTaskId(string value, out int taskId)
    {
        return CommandInputValidation.TryParsePositiveId(value, "task id", out taskId);
    }

    public static bool TryParseIds(string taskIdValue, string subtaskIdValue, out int taskId, out int subtaskId)
    {
        if (!TryParseTaskId(taskIdValue, out taskId))
        {
            subtaskId = 0;
            return false;
        }

        return CommandInputValidation.TryParsePositiveId(subtaskIdValue, "subtask id", out subtaskId);
    }
}

internal class SubtaskAddCommandSettings : CommandSettings
{
    [CommandArgument(0, "<task-id>")]
    public required string TaskId { get; set; }

    [CommandArgument(1, "<title>")]
    public required string Title { get; set; }
}

internal class SubtaskAddCommand : AsyncCommand<SubtaskAddCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SubtaskAddCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!SubtaskCommandHelpers.TryParseTaskId(settings.TaskId, out var taskId))
        {
            return 1;
        }

        if (await TodoService.GetTodoItem(taskId) is null)
        {
            Console.WriteLine($"No todo item found with id {taskId}.");
            return 1;
        }

        var subtask = await TodoService.AddSubtask(taskId, settings.Title);

        if (subtask is null)
        {
            Console.WriteLine($"No todo item found with id {taskId}.");
            return 1;
        }

        Console.WriteLine($"Added subtask #{subtask.TodoItemSubtaskId} to todo item #{taskId}: {subtask.Title}");
        return 0;
    }
}

internal class SubtaskListCommandSettings : CommandSettings
{
    [CommandArgument(0, "<task-id>")]
    public required string TaskId { get; set; }

    [CommandOption("-s|--status <status>")]
    [Description("Filter subtasks by status.")]
    public string? Status { get; set; }

    [CommandOption("-j|--json")]
    [Description("Output subtasks as JSON.")]
    public bool Json { get; set; }
}

internal class SubtaskListCommand : AsyncCommand<SubtaskListCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SubtaskListCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!SubtaskCommandHelpers.TryParseTaskId(settings.TaskId, out var taskId))
        {
            return 1;
        }

        if (await TodoService.GetTodoItem(taskId) is null)
        {
            Console.WriteLine($"No todo item found with id {taskId}.");
            return 1;
        }

        TodoItemStatus? statusFilter = null;
        if (settings.Status is not null)
        {
            if (!Enum.TryParse<TodoItemStatus>(settings.Status, ignoreCase: true, out var parsedStatus) ||
                !Enum.IsDefined(typeof(TodoItemStatus), parsedStatus))
            {
                Console.WriteLine($"Unknown status: {settings.Status}");
                return 1;
            }

            statusFilter = parsedStatus;
        }

        var subtasks = await TodoService.ListSubtasks(taskId, statusFilter);

        if (settings.Json)
        {
            var jsonSubtasks = subtasks.Select(s => new
            {
                id = s.TodoItemSubtaskId,
                taskId = s.TodoItemId,
                title = s.Title,
                status = s.Status.ToString()
            });

            Console.WriteLine(JsonSerializer.Serialize(jsonSubtasks));
            return 0;
        }

        if (subtasks.Count == 0)
        {
            Console.WriteLine($"No subtasks found for todo item #{taskId}.");
            return 0;
        }

        foreach (var subtask in subtasks)
        {
            Console.WriteLine($"#{subtask.TodoItemSubtaskId} [{subtask.Status}] of todo item #{taskId}: {subtask.Title}");
        }

        return 0;
    }
}

internal class SubtaskGetCommandSettings : CommandSettings
{
    [CommandArgument(0, "<task-id>")]
    public required string TaskId { get; set; }

    [CommandArgument(1, "<subtask-id>")]
    public required string SubtaskId { get; set; }
}

internal class SubtaskGetCommand : AsyncCommand<SubtaskGetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SubtaskGetCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!SubtaskCommandHelpers.TryParseIds(settings.TaskId, settings.SubtaskId, out var taskId, out var subtaskId))
        {
            return 1;
        }

        var subtask = await TodoService.GetSubtask(taskId, subtaskId);
        if (subtask is null)
        {
            Console.WriteLine($"No subtask found with id {subtaskId} for todo item #{taskId}.");
            return 1;
        }

        Console.WriteLine($"Subtask #{subtask.TodoItemSubtaskId} of todo item #{taskId} [{subtask.Status}]: {subtask.Title}");
        return 0;
    }
}

internal class SubtaskUpdateCommandSettings : CommandSettings
{
    [CommandArgument(0, "<task-id>")]
    public required string TaskId { get; set; }

    [CommandArgument(1, "<subtask-id>")]
    public required string SubtaskId { get; set; }

    [CommandOption("--title <title>")]
    [Description("The new title of the subtask.")]
    public string? Title { get; set; }
}

internal class SubtaskUpdateCommand : AsyncCommand<SubtaskUpdateCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SubtaskUpdateCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!SubtaskCommandHelpers.TryParseIds(settings.TaskId, settings.SubtaskId, out var taskId, out var subtaskId))
        {
            return 1;
        }

        if (settings.Title is null)
        {
            Console.WriteLine("Nothing to update. Provide --title.");
            return 1;
        }

        var subtask = await TodoService.GetSubtask(taskId, subtaskId);
        if (subtask is null)
        {
            Console.WriteLine($"No subtask found with id {subtaskId} for todo item #{taskId}.");
            return 1;
        }

        await TodoService.UpdateSubtaskTitle(taskId, subtaskId, settings.Title);
        Console.WriteLine($"Updated subtask #{subtaskId} of todo item #{taskId}: {settings.Title}");
        return 0;
    }
}

internal class SubtaskCompleteCommandSettings : CommandSettings
{
    [CommandArgument(0, "<task-id>")]
    public required string TaskId { get; set; }

    [CommandArgument(1, "<subtask-id>")]
    public required string SubtaskId { get; set; }
}

internal class SubtaskCompleteCommand : AsyncCommand<SubtaskCompleteCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SubtaskCompleteCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!SubtaskCommandHelpers.TryParseIds(settings.TaskId, settings.SubtaskId, out var taskId, out var subtaskId))
        {
            return 1;
        }

        var subtask = await TodoService.GetSubtask(taskId, subtaskId);
        if (subtask is null)
        {
            Console.WriteLine($"No subtask found with id {subtaskId} for todo item #{taskId}.");
            return 1;
        }

        await TodoService.UpdateSubtaskStatus(taskId, subtaskId, TodoItemStatus.Completed);
        Console.WriteLine($"Completed subtask #{subtaskId} of todo item #{taskId}: {subtask.Title}");
        return 0;
    }
}

internal class SubtaskDeleteCommandSettings : CommandSettings
{
    [CommandArgument(0, "<task-id>")]
    public required string TaskId { get; set; }

    [CommandArgument(1, "<subtask-id>")]
    public required string SubtaskId { get; set; }
}

internal class SubtaskDeleteCommand : AsyncCommand<SubtaskDeleteCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SubtaskDeleteCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!SubtaskCommandHelpers.TryParseIds(settings.TaskId, settings.SubtaskId, out var taskId, out var subtaskId))
        {
            return 1;
        }

        var subtask = await TodoService.DeleteSubtask(taskId, subtaskId);
        if (subtask is null)
        {
            Console.WriteLine($"No subtask found with id {subtaskId} for todo item #{taskId}.");
            return 1;
        }

        Console.WriteLine($"Deleted subtask #{subtaskId} of todo item #{taskId}: {subtask.Title}");
        return 0;
    }
}
