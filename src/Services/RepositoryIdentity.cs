using LibGit2Sharp;

namespace RepoTodo.Services;

internal static class RepositoryIdentity
{
    public static string GetRepositoryIdentifier(Repository gitRepo)
    {
        var workingDirectory = Path.GetFullPath(gitRepo.Info.WorkingDirectory);
        workingDirectory = Path.TrimEndingDirectorySeparator(workingDirectory);

        if (OperatingSystem.IsWindows())
        {
            workingDirectory = workingDirectory.ToUpperInvariant();
        }

        return $"working-tree:{workingDirectory}";
    }
}
