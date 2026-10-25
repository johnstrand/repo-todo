using Microsoft.EntityFrameworkCore;
using RepoTodo.Data;

namespace RepoTodo.Services;

internal sealed class RepositoryService(TodoContext context)
{
    public async Task<int> EnsureRepoExists(
        string repoIdentifier,
        Func<string, bool> isLegacyCommitIdentifier)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        var repo = await context.Repos.FirstOrDefaultAsync(r => r.Identifier == repoIdentifier);

        if (repo is null)
        {
            repo = new Repo { Identifier = repoIdentifier };
            context.Repos.Add(repo);
            await context.SaveChangesAsync();
        }

        var repoId = repo.RepoId;
        var oldRepositories = await context.Repos
            .Where(r => r.RepoId != repoId)
            .ToListAsync();

        foreach (var oldRepository in oldRepositories.Where(r => isLegacyCommitIdentifier(r.Identifier)))
        {
            await context.TodoItems
                .Where(t => t.RepoId == oldRepository.RepoId)
                .ExecuteUpdateAsync(items => items.SetProperty(t => t.RepoId, repoId));

            foreach (var trackedItem in context.ChangeTracker.Entries<TodoItem>()
                .Where(entry => entry.Entity.RepoId == oldRepository.RepoId))
            {
                trackedItem.Entity.RepoId = repoId;
            }

            context.Repos.Remove(oldRepository);
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return repoId;
    }
}
