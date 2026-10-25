using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace RepoTodo.Data;

internal class TodoContext : DbContext
{
    public static TodoContext Instance { get; } = new();
    public string DbPath { get; }

    public TodoContext()
    {
        var configuredDbPath = Environment.GetEnvironmentVariable("REPOTODO_DB_PATH");

        if (!string.IsNullOrWhiteSpace(configuredDbPath))
        {
            DbPath = Path.GetFullPath(configuredDbPath);
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            return;
        }

        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);

        var dbDirectory = Path.Combine(path, "RepoTodo");

        if (!Directory.Exists(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
        }

        DbPath = Path.Join(dbDirectory, "todo.db");
    }

    public DbSet<Repo> Repos => Set<Repo>();
    public DbSet<TodoItem> TodoItems => Set<TodoItem>();
    public DbSet<TodoItemSubtask> TodoItemSubtasks => Set<TodoItemSubtask>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TodoItem>()
            .Property(e => e.Tags)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null)!,
                new ValueComparer<ICollection<string>>(
                    (c1, c2) => c1!.SequenceEqual(c2!),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()));
    }
}

internal class Repo
{
    public int RepoId { get; set; }
    public required string Identifier { get; set; }
}

internal class TodoItem
{
    public int TodoItemId { get; set; }
    public int? RepoId { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public TodoItemStatus Status { get; set; }
    public ICollection<string> Tags { get; set; } = [];

    public Repo? Repo { get; set; }

    public ICollection<TodoItemSubtask> Subtasks { get; set; } = [];
}

internal class TodoItemSubtask
{
    public int TodoItemSubtaskId { get; set; }
    public int TodoItemId { get; set; }
    public required string Title { get; set; }
    public TodoItemStatus Status { get; set; }
    public TodoItem? TodoItem { get; set; }
}

internal enum TodoItemStatus
{
    NotStarted,
    Ready,
    InProgress,
    OnHold,
    Completed
}
