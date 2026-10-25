using LibGit2Sharp;
using Microsoft.EntityFrameworkCore;
using RepoTodo.Data;
using RepoTodo.Services;

try
{
    await TodoContext.Instance.Database.MigrateAsync();
    Console.WriteLine("Hello, World!");

    var repoPath = Repository.Discover(Environment.CurrentDirectory);

    if (repoPath is null)
    {
        Console.WriteLine("Not in a Git repository.");

        return 1;
    }

    Console.WriteLine($"Repository path: {repoPath}");

    var repo = new Repository(repoPath);

    Environment.CurrentDirectory = repo.Info.WorkingDirectory;

    var sessionId = SessionTracker.GetSessionId(repo);

    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
    return 1;
}
