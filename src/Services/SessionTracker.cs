using LibGit2Sharp;

namespace RepoTodo.Services;

internal static class SessionTracker
{
    public static string GetSessionId(Repository gitRepo)
    {
        if (!gitRepo.Commits.Any())
        {
            throw new InvalidOperationException("The repository has no commits.");
        }

        var headCommit = gitRepo.Head.Tip;

        if (headCommit is null)
        {
            throw new InvalidOperationException("The repository has no commits.");
        }

        return headCommit.Sha;
    }
}
