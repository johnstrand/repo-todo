using Microsoft.EntityFrameworkCore;
using RepoTodo.Data;
using System.Text.Json;

namespace RepoTodo.Services;

internal sealed class TodoService(TodoContext context, int repoId)
{
    public async Task<TodoItem> AddTodoItem(TodoItem todoItem)
    {
        todoItem.RepoId = repoId;
        context.TodoItems.Add(todoItem);
        await context.SaveChangesAsync();

        return todoItem;
    }

    public async Task<List<TodoItem>> ListTodoItems(
        TodoItemStatus? statusFilter = null,
        int? skip = null,
        int? take = null,
        string? titleFilter = null,
        string? bodyFilter = null,
        string[]? tagFilters = null)
    {
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

        itemsQuery = itemsQuery.Where(t => t.RepoId == repoId);

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

    public async Task<TodoItem?> GetTodoItem(int todoItemId)
    {
        return await context.TodoItems
            .FirstOrDefaultAsync(t => t.TodoItemId == todoItemId && t.RepoId == repoId);
    }

    public async Task<TodoItem?> GetTodoItemWithSubtasks(int todoItemId)
    {
        return await context.TodoItems
            .Where(t => t.TodoItemId == todoItemId && t.RepoId == repoId)
            .Include(t => t.Subtasks)
            .FirstOrDefaultAsync();
    }

    public async Task<TodoItemSubtask?> AddSubtask(int parentTodoItemId, string title)
    {
        var parentTodoItem = await context.TodoItems
            .FirstOrDefaultAsync(t => t.TodoItemId == parentTodoItemId && t.RepoId == repoId);

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

    public async Task<List<TodoItemSubtask>> ListSubtasks(int parentTodoItemId, TodoItemStatus? statusFilter = null)
    {
        var subtasksQuery = context.TodoItemSubtasks
            .Where(s => s.TodoItemId == parentTodoItemId &&
                s.TodoItem != null && s.TodoItem.RepoId == repoId);

        subtasksQuery = statusFilter.HasValue
            ? subtasksQuery.Where(s => s.Status == statusFilter.Value)
            : subtasksQuery.Where(s => s.Status != TodoItemStatus.Completed);

        return await subtasksQuery.OrderBy(s => s.TodoItemSubtaskId).ToListAsync();
    }

    public async Task<TodoItemSubtask?> GetSubtask(int parentTodoItemId, int subtaskId)
    {
        return await context.TodoItemSubtasks
            .FirstOrDefaultAsync(s => s.TodoItemId == parentTodoItemId &&
                s.TodoItemSubtaskId == subtaskId &&
                s.TodoItem != null && s.TodoItem.RepoId == repoId);
    }

    public async Task UpdateSubtaskTitle(int parentTodoItemId, int subtaskId, string title)
    {
        var subtask = await GetSubtask(parentTodoItemId, subtaskId);

        if (subtask is null)
        {
            return;
        }

        subtask.Title = title;
        await context.SaveChangesAsync();
    }

    public async Task UpdateTodoItem(int todoItemId, string? title = null, string? body = null)
    {
        var todoItem = await context.TodoItems
            .FirstOrDefaultAsync(t => t.TodoItemId == todoItemId && t.RepoId == repoId);

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

    public async Task UpdateTodoItemStatus(int todoItemId, TodoItemStatus status)
    {
        var todoItem = await context.TodoItems
            .FirstOrDefaultAsync(t => t.TodoItemId == todoItemId && t.RepoId == repoId);

        if (todoItem is null)
        {
            return;
        }

        todoItem.Status = status;
        await context.SaveChangesAsync();
    }

    public async Task UpdateSubtaskStatus(int parentTodoItemId, int subtaskId, TodoItemStatus status)
    {
        var subtask = await GetSubtask(parentTodoItemId, subtaskId);

        if (subtask is null)
        {
            return;
        }

        subtask.Status = status;
        await context.SaveChangesAsync();
    }

    public async Task<TodoItemSubtask?> DeleteSubtask(int parentTodoItemId, int subtaskId)
    {
        var subtask = await GetSubtask(parentTodoItemId, subtaskId);

        if (subtask is null)
        {
            return null;
        }

        context.TodoItemSubtasks.Remove(subtask);
        await context.SaveChangesAsync();

        return subtask;
    }

    public async Task DeleteTodoItem(int todoItemId)
    {
        var todoItem = await context.TodoItems
            .FirstOrDefaultAsync(t => t.TodoItemId == todoItemId && t.RepoId == repoId);

        if (todoItem is null)
        {
            return;
        }

        var syncState = await context.TodoGitHubSyncStates
            .FirstOrDefaultAsync(state => state.RepoId == repoId && state.TodoItemId == todoItemId);
        if (syncState is not null)
        {
            syncState.IsDeleted = true;
        }

        context.TodoItems.Remove(todoItem);
        await context.SaveChangesAsync();
    }
}
