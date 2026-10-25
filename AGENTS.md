# Repository guidance

## Project overview

- RepoTodo is a .NET 10 command-line application using Spectre.Console.Cli, EF Core, and SQLite.
- `src/Program.cs` bootstraps EF migrations and registers commands.
- `src/Commands/` contains CLI command/settings types; `src/Services/` contains application logic; `src/Data/TodoContext.cs` defines persistence models; `src/Migrations/` contains EF Core migrations and the model snapshot.

## Making changes

- Keep command parsing and user-facing errors consistent with adjacent commands. Invalid user input should return a nonzero command result rather than throw.
- Register new commands in `src/Program.cs` and update CLI help descriptions.
- Scope subtask operations to both the parent task ID and subtask ID.
- For schema changes, add a new EF Core migration and update the model snapshot; do not edit an already-applied migration to change the schema.
- Do not reset, delete, or otherwise alter the user's SQLite database to test code. Use isolated test databases for persistence tests.

## Verification

- Build with `dotnet build`.
- Run automated tests with `dotnet test` after making changes; add tests for new behavior and bug fixes.
- For CLI changes, check the relevant command's `--help` output and exercise both success and invalid-input paths.
