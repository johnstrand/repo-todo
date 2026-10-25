# RepoTodo

RepoTodo is a command-line todo manager for work in a Git repository. It stores todo data in a local SQLite database and supports task status, tags, bodies, and parent-scoped subtasks.

## Requirements

- .NET 10 SDK
- Run RepoTodo from inside a Git working tree. An initial commit is not required.

## Build and run

From the repository root:

```sh
dotnet build
dotnet test
dotnet run --project src/RepoTodo.csproj -- --help
```

Tests exercise the services in-process against isolated in-memory SQLite databases; they do not launch the CLI or touch the normal user database.

After publishing, install it as a global .NET tool with:

```sh
dotnet tool install --global Jst.RepoTodo
repotodo --help
```

## Publishing

The `Publish .NET tool` GitHub Actions workflow builds, tests, packs, and pushes `Jst.RepoTodo` to NuGet.org when a `v*` tag is pushed. Configure a repository secret named `NUGET_API_KEY` with a NuGet.org API key before creating a release tag (for example, `v1.0.0`). The tag, without its leading `v`, is used as the package version.

Run commands from the Git working tree whose todos you want to work with. For example:

```sh
dotnet run --project src/RepoTodo.csproj -- create "Review the deployment plan" --body "Check rollback steps" --tag ops
dotnet run --project src/RepoTodo.csproj -- list
```

Use `--` to pass arguments to RepoTodo when invoking it through `dotnet run`.

## Task commands

```text
create <title> [-b|--body <body>] [-t|--tag <tag> ...]
list [-s|--status <status>] [-j|--json] [--skip <count>] [--take <count>]
     [--title <text>] [--body <text>] [-t|--tag <tag> ...]
get <id>
update <id> [--title <title>] [--body <body>]
complete <id>
delete <id>
```

Valid task statuses are `NotStarted`, `Ready`, `InProgress`, `OnHold`, and `Completed`. By default, `list` omits completed tasks; pass `--status Completed` to list them. Title and body filters match case-insensitively. Repeated tag filters match a task with any of the supplied tags. Filters can be combined, and `--json` emits JSON including nested subtasks.

Examples:

```sh
dotnet run --project src/RepoTodo.csproj -- list --status InProgress --title "deploy"
dotnet run --project src/RepoTodo.csproj -- list --tag ops --tag release --skip 0 --take 10 --json
dotnet run --project src/RepoTodo.csproj -- get 3
dotnet run --project src/RepoTodo.csproj -- update 3 --title "Review rollout" --body "Check the rollback plan"
dotnet run --project src/RepoTodo.csproj -- complete 3
```

## Subtask commands

Every subtask operation names its parent task. Commands that target one subtask take both the parent task ID and subtask ID:

```text
subtask add <task-id> <title>
subtask list <task-id> [-s|--status <status>] [-j|--json]
subtask get <task-id> <subtask-id>
subtask update <task-id> <subtask-id> --title <title>
subtask complete <task-id> <subtask-id>
subtask delete <task-id> <subtask-id>
```

By default, `subtask list` omits completed subtasks; pass `--status Completed` to include only completed subtasks.

Example:

```sh
dotnet run --project src/RepoTodo.csproj -- subtask add 3 "Verify rollback procedure"
dotnet run --project src/RepoTodo.csproj -- subtask list 3
```

## Data storage

The SQLite database is created under the current user's .NET `LocalApplicationData` directory, in a `RepoTodo/todo.db` subdirectory. Set `REPOTODO_DB_PATH` to use a specific database file (for example, for tests). EF Core migrations are applied automatically when the application starts. Todo data is associated with the local working-tree path, so separate clones or worktrees have separate lists. Legacy data associated with commits in a working tree is migrated to its stable identity on first use.
