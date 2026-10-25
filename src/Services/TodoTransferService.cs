using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RepoTodo.Data;

namespace RepoTodo.Services;

internal sealed class TodoTransferService(TodoContext context, int repoId)
{
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<int> ExportAsync(Stream destination)
    {
        var items = await context.TodoItems
            .Where(item => item.RepoId == repoId)
            .OrderBy(item => item.TodoItemId)
            .Include(item => item.Subtasks)
            .ToListAsync();

        var document = new TodoExportDocument
        {
            FormatVersion = CurrentFormatVersion,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Items = items.Select(item => new TodoExportItem
            {
                Title = item.Title,
                Body = item.Body,
                Status = item.Status.ToString(),
                Tags = item.Tags.ToList(),
                Subtasks = item.Subtasks
                    .OrderBy(subtask => subtask.TodoItemSubtaskId)
                    .Select(subtask => new TodoExportSubtask
                    {
                        Title = subtask.Title,
                        Status = subtask.Status.ToString()
                    })
                    .ToList()
            }).ToList()
        };

        await JsonSerializer.SerializeAsync(destination, document, JsonOptions);
        return items.Count;
    }

    public async Task<int> ImportAsync(Stream source)
    {
        var document = await JsonSerializer.DeserializeAsync<TodoExportDocument>(source, JsonOptions)
            ?? throw new InvalidDataException("The import file does not contain a JSON document.");

        Validate(document);

        await using var transaction = await context.Database.BeginTransactionAsync();
        var items = document.Items.Select(item => new TodoItem
        {
            RepoId = repoId,
            Title = item.Title,
            Body = item.Body,
            Status = ParseStatus(item.Status),
            Tags = item.Tags.ToList(),
            Subtasks = item.Subtasks.Select(subtask => new TodoItemSubtask
            {
                Title = subtask.Title,
                Status = ParseStatus(subtask.Status)
            }).ToList()
        }).ToList();

        context.TodoItems.AddRange(items);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return items.Count;
    }

    private static void Validate(TodoExportDocument document)
    {
        if (document.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported export format version: {document.FormatVersion}. Supported version: {CurrentFormatVersion}.");
        }

        if (document.Items is null)
        {
            throw new InvalidDataException("The export document is missing its items collection.");
        }

        foreach (var item in document.Items)
        {
            if (item is null)
            {
                throw new InvalidDataException("The export document contains a null todo item.");
            }

            if (string.IsNullOrWhiteSpace(item.Title) || item.Body is null || item.Tags is null || item.Subtasks is null)
            {
                throw new InvalidDataException("Each exported todo must have a title, body, tags, and subtasks collection.");
            }

            if (item.Tags.Any(tag => tag is null))
            {
                throw new InvalidDataException("Todo tags cannot be null.");
            }

            _ = ParseStatus(item.Status);

            foreach (var subtask in item.Subtasks)
            {
                if (subtask is null || string.IsNullOrWhiteSpace(subtask.Title))
                {
                    throw new InvalidDataException("Each exported subtask must be present and have a title.");
                }

                _ = ParseStatus(subtask.Status);
            }
        }
    }

    private static TodoItemStatus ParseStatus(string? value)
    {
        if (value is not null &&
            Enum.TryParse<TodoItemStatus>(value, ignoreCase: true, out var status) &&
            Enum.IsDefined(status))
        {
            return status;
        }

        throw new InvalidDataException($"Unknown todo status in import file: {value ?? "<null>"}.");
    }
}

internal sealed class TodoExportDocument
{
    public required int FormatVersion { get; init; }
    public required DateTimeOffset ExportedAtUtc { get; init; }
    public required List<TodoExportItem> Items { get; init; }
}

internal sealed class TodoExportItem
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required string Status { get; init; }
    public required List<string> Tags { get; init; }
    public required List<TodoExportSubtask> Subtasks { get; init; }
}

internal sealed class TodoExportSubtask
{
    public required string Title { get; init; }
    public required string Status { get; init; }
}
