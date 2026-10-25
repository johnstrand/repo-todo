using RepoTodo.Services;
using Spectre.Console.Cli;

namespace RepoTodo.Commands;

internal abstract class TodoAsyncCommand<TSettings> : AsyncCommand<TSettings>
    where TSettings : CommandSettings
{
    protected TodoAsyncCommand(TodoService todoService)
    {
        TodoService = todoService;
    }

    protected TodoService TodoService { get; }
}
