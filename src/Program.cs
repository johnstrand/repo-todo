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

    var repoIdentifier = RepositoryIdentity.GetRepositoryIdentifier(repo);
    await TodoService.EnsureRepoExists(repoIdentifier, identifier =>
        identifier.Length == 40 &&
        identifier.All(Uri.IsHexDigit) &&
        repo.Lookup<Commit>(identifier) is not null);

    var app = new CommandApp();

    app.Configure(config =>
    {
        config.SetApplicationName("RepoTodo");
        config.AddCommand<RepoTodo.Commands.CreateCommand>("create")
            .WithDescription("Create a new todo item.");
        config.AddBranch("subtask", branch =>
        {
            branch.SetDescription("Manage todo subtasks.");
            branch.AddCommand<RepoTodo.Commands.SubtaskAddCommand>("add")
                .WithDescription("Add a subtask to a todo item.");
            branch.AddCommand<RepoTodo.Commands.SubtaskListCommand>("list")
                .WithDescription("List subtasks for a todo item.");
            branch.AddCommand<RepoTodo.Commands.SubtaskGetCommand>("get")
                .WithDescription("Get a subtask.");
            branch.AddCommand<RepoTodo.Commands.SubtaskUpdateCommand>("update")
                .WithDescription("Update a subtask.");
            branch.AddCommand<RepoTodo.Commands.SubtaskCompleteCommand>("complete")
                .WithDescription("Mark a subtask as completed.");
            branch.AddCommand<RepoTodo.Commands.SubtaskDeleteCommand>("delete")
                .WithDescription("Delete a subtask.");
        });
        config.AddCommand<RepoTodo.Commands.ListCommand>("list")
            .WithDescription("List todo items.");
        config.AddCommand<RepoTodo.Commands.GetCommand>("get")
            .WithDescription("Get a single todo item.");
        config.AddCommand<RepoTodo.Commands.UpdateCommand>("update")
            .WithDescription("Update a todo item.");
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
