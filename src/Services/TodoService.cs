using Microsoft.EntityFrameworkCore;
using RepoTodo.Data;

namespace RepoTodo.Services;

internal static class TodoService
{
    public static int RepoId { get; set; }

    public static void EnsureRepoExists(string repoIdentifier)
    {
        var context = TodoContext.Instance;
        var existingRepo = context.Remotes.FirstOrDefault(r => r.Identifier == repoIdentifier);

        if (existingRepo is null)
        {
            var newRepo = new Repo { Identifier = repoIdentifier };
            context.Remotes.Add(newRepo);
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

    public static async Task<List<TodoItem>> ListTodoItems(TodoItemStatus? statusFilter = null)
    {
        var context = TodoContext.Instance;
        var itemsQuery = context.TodoItems.AsQueryable();

        if (statusFilter.HasValue)
        {
            itemsQuery = itemsQuery.Where(t => t.Status == statusFilter.Value);
        }
        else
        {
            itemsQuery = itemsQuery.Where(t => t.Status != TodoItemStatus.Completed);
        }

        return await itemsQuery.OrderBy(t => t.TodoItemId).ToListAsync();
    }

    public static async Task<TodoItem?> GetTodoItem(int todoItemId)
    {
        var context = TodoContext.Instance;

        return await context.TodoItems.FirstOrDefaultAsync(t => t.TodoItemId == todoItemId);
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