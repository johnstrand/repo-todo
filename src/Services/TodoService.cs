using Microsoft.EntityFrameworkCore;
using RepoTodo.Data;
using System.Text.Json;

namespace RepoTodo.Services;

internal static class TodoService
{
    public static int RepoId { get; set; }

    public static async Task EnsureRepoExists(string repoIdentifier, Func<string, bool> isLegacyCommitIdentifier)
    {
        var context = TodoContext.Instance;
        await using var transaction = await context.Database.BeginTransactionAsync();
        var existingRepo = await context.Repos.FirstOrDefaultAsync(r => r.Identifier == repoIdentifier);

        if (existingRepo is null)
        {
            var newRepo = new Repo { Identifier = repoIdentifier };
            context.Repos.Add(newRepo);
            await context.SaveChangesAsync();
            existingRepo = newRepo;
        }

        RepoId = existingRepo.RepoId;

        var oldRepositories = await context.Repos
            .Where(r => r.RepoId != RepoId)
            .ToListAsync();

        foreach (var oldRepository in oldRepositories.Where(r => isLegacyCommitIdentifier(r.Identifier)))
        {
            await context.TodoItems
                .Where(t => t.RepoId == oldRepository.RepoId)
                .ExecuteUpdateAsync(items => items.SetProperty(t => t.RepoId, RepoId));

            context.Repos.Remove(oldRepository);
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public static async Task<TodoItem> AddTodoItem(TodoItem todoItem)
    {
        var context = TodoContext.Instance;
        todoItem.RepoId = RepoId;
        context.TodoItems.Add(todoItem);
        await context.SaveChangesAsync();

        return todoItem;
    }

    public static async Task<List<TodoItem>> ListTodoItems(
        TodoItemStatus? statusFilter = null,
        int? skip = null,
        int? take = null,
        string? titleFilter = null,
        string? bodyFilter = null,
        string[]? tagFilters = null)
    {
        var context = TodoContext.Instance;
        IQueryable<TodoItem> itemsQuery;

        if (tagFilters is { Length: > 0 })
        {
            var tagValuesJson = JsonSerializer.Serialize(tagFilters);
            itemsQuery = context.TodoItems.FromSqlInterpolated($"""
                SELECT * FROM "TodoItems" AS item
                WHERE EXISTS (
                    SELECT 1
                    FROM json_each(item."Tags") AS storedTag
                    WHERE storedTag.value IN (
                        SELECT value FROM json_each({tagValuesJson})
                    )
                )
                """);
        }
        else
        {
            itemsQuery = context.TodoItems;
        }

        itemsQuery = itemsQuery.Where(t => t.RepoId == RepoId);

        IQueryable<TodoItem> filteredQuery = statusFilter.HasValue
            ? itemsQuery.Where(t => t.Status == statusFilter.Value)
            : itemsQuery.Where(t => t.Status != TodoItemStatus.Completed);

        if (titleFilter is not null)
        {
            var titlePattern = ToContainsPattern(titleFilter);
            filteredQuery = filteredQuery.Where(t => EF.Functions.Like(t.Title, titlePattern, "\\"));
        }

        if (bodyFilter is not null)
        {
            var bodyPattern = ToContainsPattern(bodyFilter);
            filteredQuery = filteredQuery.Where(t => EF.Functions.Like(t.Body, bodyPattern, "\\"));
        }

        filteredQuery = filteredQuery.OrderBy(t => t.TodoItemId);

        if (skip.HasValue)
        {
            filteredQuery = filteredQuery.Skip(skip.Value);
        }

        if (take.HasValue)
        {
            filteredQuery = filteredQuery.Take(take.Value);
        }

        return await filteredQuery.Include(t => t.Subtasks).ToListAsync();
    }

    private static string ToContainsPattern(string value)
    {
        var escapedValue = value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        return $"%{escapedValue}%";
    }

    public static async Task<TodoItem?> GetTodoItem(int todoItemId)
    {
        var context = TodoContext.Instance;

        return await context.TodoItems
            .FirstOrDefaultAsync(t => t.TodoItemId == todoItemId && t.RepoId == RepoId);
    }

    public static async Task<TodoItem?> GetTodoItemWithSubtasks(int todoItemId)
    {
        return await TodoContext.Instance.TodoItems
            .Where(t => t.TodoItemId == todoItemId && t.RepoId == RepoId)
            .Include(t => t.Subtasks)
            .FirstOrDefaultAsync();
    }

    public static async Task<TodoItemSubtask?> AddSubtask(int parentTodoItemId, string title)
    {
        var context = TodoContext.Instance;
        var parentTodoItem = await context.TodoItems
            .FirstOrDefaultAsync(t => t.TodoItemId == parentTodoItemId && t.RepoId == RepoId);

        if (parentTodoItem is null)
        {
            return null;
        }

        var subtask = new TodoItemSubtask
        {
            TodoItemId = parentTodoItemId,
            Title = title,
            Status = TodoItemStatus.NotStarted,
            TodoItem = parentTodoItem
        };

        context.TodoItemSubtasks.Add(subtask);
        await context.SaveChangesAsync();

        return subtask;
    }

    public static async Task<List<TodoItemSubtask>> ListSubtasks(int parentTodoItemId, TodoItemStatus? statusFilter = null)
    {
        var subtasksQuery = TodoContext.Instance.TodoItemSubtasks
            .Where(s => s.TodoItemId == parentTodoItemId &&
                s.TodoItem != null && s.TodoItem.RepoId == RepoId);

        subtasksQuery = statusFilter.HasValue
            ? subtasksQuery.Where(s => s.Status == statusFilter.Value)
            : subtasksQuery.Where(s => s.Status != TodoItemStatus.Completed);

        return await subtasksQuery.OrderBy(s => s.TodoItemSubtaskId).ToListAsync();
    }

    public static async Task<TodoItemSubtask?> GetSubtask(int parentTodoItemId, int subtaskId)
    {
        return await TodoContext.Instance.TodoItemSubtasks
            .FirstOrDefaultAsync(s => s.TodoItemId == parentTodoItemId &&
                s.TodoItemSubtaskId == subtaskId &&
                s.TodoItem != null && s.TodoItem.RepoId == RepoId);
    }

    public static async Task UpdateSubtaskTitle(int parentTodoItemId, int subtaskId, string title)
    {
        var subtask = await GetSubtask(parentTodoItemId, subtaskId);

        if (subtask is null)
        {
            return;
        }

        subtask.Title = title;
        await TodoContext.Instance.SaveChangesAsync();
    }

    public static async Task UpdateTodoItem(int todoItemId, string? title = null, string? body = null)
    {
        var context = TodoContext.Instance;
        var todoItem = await context.TodoItems
            .FirstOrDefaultAsync(t => t.TodoItemId == todoItemId && t.RepoId == RepoId);

        if (todoItem is null)
        {
            return;
        }

        if (title is not null)
        {
            todoItem.Title = title;
        }

        if (body is not null)
        {
            todoItem.Body = body;
        }

        await context.SaveChangesAsync();
    }

    public static async Task UpdateTodoItemStatus(int todoItemId, TodoItemStatus status)
    {
        var context = TodoContext.Instance;
        var todoItem = await context.TodoItems
            .FirstOrDefaultAsync(t => t.TodoItemId == todoItemId && t.RepoId == RepoId);

        if (todoItem is null)
        {
            return;
        }

        todoItem.Status = status;
        await context.SaveChangesAsync();
    }

    public static async Task UpdateSubtaskStatus(int parentTodoItemId, int subtaskId, TodoItemStatus status)
    {
        var subtask = await GetSubtask(parentTodoItemId, subtaskId);

        if (subtask is null)
        {
            return;
        }

        subtask.Status = status;
        await TodoContext.Instance.SaveChangesAsync();
    }

    public static async Task<TodoItemSubtask?> DeleteSubtask(int parentTodoItemId, int subtaskId)
    {
        var context = TodoContext.Instance;
        var subtask = await GetSubtask(parentTodoItemId, subtaskId);

        if (subtask is null)
        {
            return null;
        }

        context.TodoItemSubtasks.Remove(subtask);
        await context.SaveChangesAsync();

        return subtask;
    }

    public static async Task DeleteTodoItem(int todoItemId)
    {
        var context = TodoContext.Instance;
        var todoItem = await context.TodoItems
            .FirstOrDefaultAsync(t => t.TodoItemId == todoItemId && t.RepoId == RepoId);

        if (todoItem is null)
        {
            return;
        }

        context.TodoItems.Remove(todoItem);
        await context.SaveChangesAsync();
    }
}
