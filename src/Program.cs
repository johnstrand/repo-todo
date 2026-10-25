using LibGit2Sharp;
using Microsoft.EntityFrameworkCore;
using RepoTodo.Data;
using RepoTodo.Services;
using Spectre.Console.Cli;

try
{
    await TodoContext.Instance.Database.MigrateAsync();

    var repoPath = Repository.Discover(Environment.CurrentDirectory);

    if (repoPath is null)
    {
        Console.WriteLine("Not in a Git repository.");

        return 1;
    }

    var repo = new Repository(repoPath);

    Environment.CurrentDirectory = repo.Info.WorkingDirectory;

    var sessionId = SessionTracker.GetSessionId(repo);

    TodoService.EnsureRepoExists(sessionId);

    var app = new CommandApp();

    app.Configure(config =>
    {
        config.SetApplicationName("RepoTodo");
        config.AddCommand<RepoTodo.Commands.CreateCommand>("create")
            .WithDescription("Create a new todo item.");
        config.AddCommand<RepoTodo.Commands.ListCommand>("list")
            .WithDescription("List todo items.");
        config.AddCommand<RepoTodo.Commands.CompleteCommand>("complete")
            .WithDescription("Mark a todo item as completed.");
        config.AddCommand<RepoTodo.Commands.DeleteCommand>("delete")
            .WithDescription("Delete a todo item.");
    });

    return await app.RunAsync(args);
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
    return 1;
}
