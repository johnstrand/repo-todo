using LibGit2Sharp;
using Microsoft.EntityFrameworkCore;
using RepoTodo.Commands;
using RepoTodo.Data;
using RepoTodo.Services;

try
{
    var configuredDbPath = Environment.GetEnvironmentVariable("REPOTODO_DB_PATH");
    var dbPath = string.IsNullOrWhiteSpace(configuredDbPath)
        ? TodoContext.GetDefaultDbPath()
        : Path.GetFullPath(configuredDbPath);
    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

    var dbOptions = new DbContextOptionsBuilder<TodoContext>()
        .UseSqlite($"Data Source={dbPath}")
        .Options;
    await using var todoContext = new TodoContext(dbOptions);
    await todoContext.Database.MigrateAsync();

    var repoPath = Repository.Discover(Environment.CurrentDirectory);

    if (repoPath is null)
    {
        Console.WriteLine("Not in a Git repository.");

        return 1;
    }

    var repo = new Repository(repoPath);

    Environment.CurrentDirectory = repo.Info.WorkingDirectory;

    var repoIdentifier = RepositoryIdentity.GetRepositoryIdentifier(repo);
    var repoId = await new RepositoryService(todoContext).EnsureRepoExists(repoIdentifier, identifier =>
        identifier.Length == 40 &&
        identifier.All(Uri.IsHexDigit) &&
        repo.Lookup<Commit>(identifier) is not null);

    using var githubHttpClient = new HttpClient();
    var githubSyncService = new GitHubIssueSyncService(
        todoContext,
        repoId,
        GitHubRepository.FromRemoteUrls(repo.Network.Remotes.Select(remote => (remote.Name, remote.Url))),
        Environment.GetEnvironmentVariable("TODO_GITHUB_TOKEN"),
        githubHttpClient);
    var app = TodoCommandApp.Create(
        new TodoService(todoContext, repoId),
        new TodoTransferService(todoContext, repoId),
        githubSyncService);
    return await app.RunAsync(args);
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
    return 1;
}
