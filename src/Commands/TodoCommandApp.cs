using RepoTodo.Services;
using Spectre.Console.Cli;

namespace RepoTodo.Commands;

internal static class TodoCommandApp
{
    public static CommandApp Create(TodoService todoService, TodoTransferService transferService)
    {
        var typeRegistrar = new TypeRegistrar();
        typeRegistrar.RegisterInstance(typeof(TodoService), todoService);
        typeRegistrar.RegisterInstance(typeof(TodoTransferService), transferService);
        var app = new CommandApp(typeRegistrar);

        app.Configure(config =>
        {
            config.SetApplicationName("RepoTodo");
            config.AddCommand<CreateCommand>("create")
                .WithDescription("Create a new todo item.");
            config.AddBranch("subtask", branch =>
            {
                branch.SetDescription("Manage todo subtasks.");
                branch.AddCommand<SubtaskAddCommand>("add")
                    .WithDescription("Add a subtask to a todo item.");
                branch.AddCommand<SubtaskListCommand>("list")
                    .WithDescription("List subtasks for a todo item.");
                branch.AddCommand<SubtaskGetCommand>("get")
                    .WithDescription("Get a subtask.");
                branch.AddCommand<SubtaskUpdateCommand>("update")
                    .WithDescription("Update a subtask.");
                branch.AddCommand<SubtaskCompleteCommand>("complete")
                    .WithDescription("Mark a subtask as completed.");
                branch.AddCommand<SubtaskDeleteCommand>("delete")
                    .WithDescription("Delete a subtask.");
            });
            config.AddCommand<ListCommand>("list")
                .WithDescription("List todo items.");
            config.AddCommand<ExportCommand>("export")
                .WithDescription("Export todo items and subtasks as versioned JSON.");
            config.AddCommand<ImportCommand>("import")
                .WithDescription("Import todo items and subtasks from a JSON export.");
            config.AddCommand<GetCommand>("get")
                .WithDescription("Get a single todo item.");
            config.AddCommand<UpdateCommand>("update")
                .WithDescription("Update a todo item.");
            config.AddCommand<CompleteCommand>("complete")
                .WithDescription("Mark a todo item as completed.");
            config.AddCommand<DeleteCommand>("delete")
                .WithDescription("Delete a todo item.");
        });

        return app;
    }
}
