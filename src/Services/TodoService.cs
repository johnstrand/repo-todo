using Microsoft.EntityFrameworkCore;
using RepoTodo.Data;

namespace RepoTodo.Services;

internal static class TodoService
{
    public static int RepoId { get; set; }

    public static void EnsureRepoExists(string repoIdentifier)
    {
        var context = TodoContext.Instance;
        var existingRepo = context.Repos.FirstOrDefault(r => r.Identifier == repoIdentifier);

        if (existingRepo is null)
        {
            var newRepo = new Repo { Identifier = repoIdentifier };
            context.Repos.Add(newRepo);
            context.SaveChanges();
            RepoId = newRepo.RepoId;
        }
        else
        {
            RepoId = existingRepo.RepoId;
        }
    }

    public static async Task<TodoItem> AddTodoItem(TodoItem todoItem)
    {
        var context = TodoContext.Instance;
        context.TodoItems.Add(todoItem);
        await context.SaveChangesAsync();

        return todoItem;
    }

    public static async Task<List<TodoItem>> ListTodoItems(TodoItemStatus? statusFilter = null, int? skip = null, int? take = null)
    {
        var context = TodoContext.Instance;
        var itemsQuery = context.TodoItems.AsQueryable().Include(t => t.Subtasks);

        IQueryable<TodoItem> filteredQuery = statusFilter.HasValue
            ? itemsQuery.Where(t => t.Status == statusFilter.Value)
            : itemsQuery.Where(t => t.Status != TodoItemStatus.Completed);

        if (skip.HasValue)
        {
            filteredQuery = filteredQuery.Skip(skip.Value);
        }

        if (take.HasValue)
        {
            filteredQuery = filteredQuery.Take(take.Value);
        }

        return await filteredQuery.OrderBy(t => t.TodoItemId).ToListAsync();
    }

    public static async Task<TodoItem?> GetTodoItem(int todoItemId)
    {
        var context = TodoContext.Instance;

        return await context.TodoItems.FirstOrDefaultAsync(t => t.TodoItemId == todoItemId);
    }

    public static async Task<TodoItemSubtask?> AddSubtask(int parentTodoItemId, string title)
    {
        var context = TodoContext.Instance;
        var parentTodoItem = await context.TodoItems.FirstOrDefaultAsync(t => t.TodoItemId == parentTodoItemId);

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

    public static async Task<TodoItemSubtask?> GetSubtask(int subtaskId)
    {
        var context = TodoContext.Instance;

        return await context.TodoItemSubtasks.FirstOrDefaultAsync(s => s.TodoItemSubtaskId == subtaskId);
    }

    public static async Task UpdateTodoItem(int todoItemId, string? title = null, string? body = null)
    {
        var context = TodoContext.Instance;
        var todoItem = await context.TodoItems.FirstOrDefaultAsync(t => t.TodoItemId == todoItemId);

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
        var todoItem = await context.TodoItems.FirstOrDefaultAsync(t => t.TodoItemId == todoItemId);

        if (todoItem is null)
        {
            return;
        }

        todoItem.Status = status;
        await context.SaveChangesAsync();
    }

    public static async Task UpdateSubtaskStatus(int subtaskId, TodoItemStatus status)
    {
        var context = TodoContext.Instance;
        var subtask = await context.TodoItemSubtasks.FirstOrDefaultAsync(s => s.TodoItemSubtaskId == subtaskId);

        if (subtask is null)
        {
            return;
        }

        subtask.Status = status;
        await context.SaveChangesAsync();
    }

    public static async Task DeleteTodoItem(int todoItemId)
    {
        var context = TodoContext.Instance;
        var todoItem = await context.TodoItems.FirstOrDefaultAsync(t => t.TodoItemId == todoItemId);

        if (todoItem is null)
        {
            return;
        }

        context.TodoItems.Remove(todoItem);
        await context.SaveChangesAsync();
    }
}