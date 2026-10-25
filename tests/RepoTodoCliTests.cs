using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RepoTodo.Tests;

[TestClass]
public sealed class RepoTodoCliTests
{
    [TestMethod]
    public async Task CliSupportsTaskSubtaskFilteringAndPaginationFlows()
    {
        using var testEnvironment = new TestEnvironment();
        var repository = testEnvironment.CreateRepository("commands");

        var createResult = await repository.RunAsync(
            "create", "Parent deployment", "--body", "Review rollback plan", "--tag", "ops", "--tag", "release");
        AssertSuccess(createResult);
        var taskId = ReadCreatedId(createResult.Output, "Created todo item");

        var addSubtaskResult = await repository.RunAsync("subtask", "add", taskId.ToString(), "Check migrations");
        AssertSuccess(addSubtaskResult);
        var subtaskId = ReadCreatedId(addSubtaskResult.Output, "Added subtask");

        var detailResult = await repository.RunAsync("get", taskId.ToString());
        AssertSuccess(detailResult);
        StringAssert.Contains(detailResult.Output, "Check migrations");

        AssertSuccess(await repository.RunAsync("subtask", "update", taskId.ToString(), subtaskId.ToString(), "--title", "Verify migration plan"));
        AssertSuccess(await repository.RunAsync("subtask", "complete", taskId.ToString(), subtaskId.ToString()));

        var openSubtasks = await repository.RunAsync("subtask", "list", taskId.ToString(), "--json");
        AssertSuccess(openSubtasks);
        Assert.AreEqual(0, ReadJsonArray(openSubtasks.Output).GetArrayLength());

        var completedSubtasks = await repository.RunAsync("subtask", "list", taskId.ToString(), "--status", "Completed", "--json");
        AssertSuccess(completedSubtasks);
        Assert.AreEqual("Verify migration plan", ReadJsonArray(completedSubtasks.Output)[0].GetProperty("title").GetString());

        var secondTaskResult = await repository.RunAsync("create", "Parent follow-up", "--body", "Deployment checklist", "--tag", "maintenance");
        AssertSuccess(secondTaskResult);
        var secondTaskId = ReadCreatedId(secondTaskResult.Output, "Created todo item");

        var filtered = await repository.RunAsync(
            "list", "--title", "PARENT", "--body", "rollback", "--tag", "missing", "--tag", "ops", "--json");
        AssertSuccess(filtered);
        var filteredItems = ReadJsonArray(filtered.Output);
        Assert.AreEqual(1, filteredItems.GetArrayLength());
        Assert.AreEqual(taskId, filteredItems[0].GetProperty("id").GetInt32());

        var firstPage = await repository.RunAsync("list", "--title", "parent", "--take", "1", "--json");
        AssertSuccess(firstPage);
        Assert.AreEqual(taskId, ReadJsonArray(firstPage.Output)[0].GetProperty("id").GetInt32());

        var secondPage = await repository.RunAsync("list", "--title", "parent", "--skip", "1", "--take", "1", "--json");
        AssertSuccess(secondPage);
        Assert.AreEqual(secondTaskId, ReadJsonArray(secondPage.Output)[0].GetProperty("id").GetInt32());

        AssertSuccess(await repository.RunAsync("update", taskId.ToString(), "--title", "Updated deployment"));
        AssertSuccess(await repository.RunAsync("subtask", "delete", taskId.ToString(), subtaskId.ToString()));
        AssertSuccess(await repository.RunAsync("delete", secondTaskId.ToString()));
    }

    [TestMethod]
    public async Task RepositoryIdentityIsStableAndDataIsScopedAcrossWorkingTrees()
    {
        using var testEnvironment = new TestEnvironment();
        var firstRepository = testEnvironment.CreateRepository("first");

        var createResult = await firstRepository.RunAsync("create", "Only in first working tree");
        AssertSuccess(createResult);
        var firstTaskId = ReadCreatedId(createResult.Output, "Created todo item");

        firstRepository.CreateCommit();
        var firstList = await firstRepository.RunAsync("list", "--title", "Only in first", "--json");
        AssertSuccess(firstList);
        Assert.AreEqual(firstTaskId, ReadJsonArray(firstList.Output)[0].GetProperty("id").GetInt32());

        var secondRepository = testEnvironment.CreateRepository("second");
        var secondList = await secondRepository.RunAsync("list", "--json");
        AssertSuccess(secondList);
        Assert.AreEqual(0, ReadJsonArray(secondList.Output).GetArrayLength());

        var crossRepositoryGet = await secondRepository.RunAsync("get", firstTaskId.ToString());
        Assert.AreNotEqual(0, crossRepositoryGet.ExitCode);
        StringAssert.Contains(crossRepositoryGet.Output, "No todo item found");

        var crossRepositoryUpdate = await secondRepository.RunAsync("update", firstTaskId.ToString(), "--title", "Must not update across repositories");
        Assert.AreNotEqual(0, crossRepositoryUpdate.ExitCode);
        StringAssert.Contains(crossRepositoryUpdate.Output, "No todo item found");

        var crossRepositoryComplete = await secondRepository.RunAsync("complete", firstTaskId.ToString());
        Assert.AreNotEqual(0, crossRepositoryComplete.ExitCode);
        StringAssert.Contains(crossRepositoryComplete.Output, "No todo item found");

        var crossRepositoryDelete = await secondRepository.RunAsync("delete", firstTaskId.ToString());
        Assert.AreNotEqual(0, crossRepositoryDelete.ExitCode);
        StringAssert.Contains(crossRepositoryDelete.Output, "No todo item found");

        var firstListAfterCrossRepositoryWrites = await firstRepository.RunAsync("list", "--json");
        AssertSuccess(firstListAfterCrossRepositoryWrites);
        Assert.AreEqual("Only in first working tree", ReadJsonArray(firstListAfterCrossRepositoryWrites.Output)[0].GetProperty("title").GetString());

        var secondCreateResult = await secondRepository.RunAsync("create", "Only in second working tree");
        AssertSuccess(secondCreateResult);

        var firstListAfterSecondCreate = await firstRepository.RunAsync("list", "--json");
        AssertSuccess(firstListAfterSecondCreate);
        Assert.AreEqual(1, ReadJsonArray(firstListAfterSecondCreate.Output).GetArrayLength());
        Assert.AreEqual(firstTaskId, ReadJsonArray(firstListAfterSecondCreate.Output)[0].GetProperty("id").GetInt32());
    }

    [TestMethod]
    public async Task UnbornRepositoryWorksAndLegacyCommitIdentityMigratesTasks()
    {
        using var testEnvironment = new TestEnvironment();
        var repository = testEnvironment.CreateRepository("legacy");

        var createResult = await repository.RunAsync("create", "Preserve legacy task");
        AssertSuccess(createResult);
        var taskId = ReadCreatedId(createResult.Output, "Created todo item");

        var commitSha = repository.CreateCommit();
        await RewriteRepositoryIdentifierAsLegacyCommitAsync(testEnvironment.DatabasePath, commitSha);

        var listAfterMigration = await repository.RunAsync("list", "--title", "Preserve legacy", "--json");
        AssertSuccess(listAfterMigration);
        Assert.AreEqual(taskId, ReadJsonArray(listAfterMigration.Output)[0].GetProperty("id").GetInt32());

        repository.CreateCommit();
        var listAfterAnotherCommit = await repository.RunAsync("list", "--title", "Preserve legacy", "--json");
        AssertSuccess(listAfterAnotherCommit);
        Assert.AreEqual(taskId, ReadJsonArray(listAfterAnotherCommit.Output)[0].GetProperty("id").GetInt32());
    }

    [TestMethod]
    public async Task InvalidIdsStatusesAndPaginationReturnUsefulErrors()
    {
        using var testEnvironment = new TestEnvironment();
        var repository = testEnvironment.CreateRepository("validation");

        var invalidDelete = await repository.RunAsync("delete", "not-an-id");
        Assert.AreNotEqual(0, invalidDelete.ExitCode);
        StringAssert.Contains(invalidDelete.Output, "Invalid todo item id");

        var invalidTaskId = await repository.RunAsync("get", "0");
        Assert.AreNotEqual(0, invalidTaskId.ExitCode);
        StringAssert.Contains(invalidTaskId.Output, "Invalid todo item id");

        var invalidStatus = await repository.RunAsync("list", "--status", "99");
        Assert.AreNotEqual(0, invalidStatus.ExitCode);
        StringAssert.Contains(invalidStatus.Output, "Unknown status");

        var taskResult = await repository.RunAsync("create", "Validation parent");
        AssertSuccess(taskResult);
        var taskId = ReadCreatedId(taskResult.Output, "Created todo item");

        var invalidSubtaskStatus = await repository.RunAsync("subtask", "list", taskId.ToString(), "--status", "99");
        Assert.AreNotEqual(0, invalidSubtaskStatus.ExitCode);
        StringAssert.Contains(invalidSubtaskStatus.Output, "Unknown status");

        var invalidSkip = await repository.RunAsync("list", "--skip", "-1");
        Assert.AreNotEqual(0, invalidSkip.ExitCode);
        StringAssert.Contains(invalidSkip.Output, "--skip must be zero or greater");

        var invalidTake = await repository.RunAsync("list", "--take", "-1");
        Assert.AreNotEqual(0, invalidTake.ExitCode);
        StringAssert.Contains(invalidTake.Output, "--take must be zero or greater");
    }

    private static void AssertSuccess(CommandResult result)
    {
        Assert.AreEqual(0, result.ExitCode, result.Output);
    }

    private static int ReadCreatedId(string output, string prefix)
    {
        var match = Regex.Match(output, $@"{Regex.Escape(prefix)} #(\d+)");
        Assert.IsTrue(match.Success, $"Could not find a created ID in output: {output}");
        return int.Parse(match.Groups[1].Value);
    }

    private static JsonElement ReadJsonArray(string output)
    {
        var jsonLine = output.Split(Environment.NewLine)
            .LastOrDefault(line => line.TrimStart().StartsWith("[", StringComparison.Ordinal));
        Assert.IsNotNull(jsonLine, $"Could not find JSON output: {output}");
        using var document = JsonDocument.Parse(jsonLine);
        return document.RootElement.Clone();
    }

    private static async Task RewriteRepositoryIdentifierAsLegacyCommitAsync(string databasePath, string commitSha)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Repos
            SET Identifier = $commitSha
            WHERE RepoId = (SELECT RepoId FROM Repos ORDER BY RepoId LIMIT 1)
            """;
        command.Parameters.AddWithValue("$commitSha", commitSha);
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
    }

    private sealed class TestEnvironment : IDisposable
    {
        private readonly string _root;
        private readonly string _repoTodoExecutable;

        public string DatabasePath { get; }

        public TestEnvironment()
        {
            _root = Path.Combine(Path.GetTempPath(), "RepoTodo.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            DatabasePath = Path.Combine(_root, "todos.db");
            var buildConfiguration = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.Name
                ?? throw new DirectoryNotFoundException("Could not determine the test build configuration.");
            var targetFramework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            var executableName = OperatingSystem.IsWindows() ? "RepoTodo.exe" : "RepoTodo";
            _repoTodoExecutable = Path.Combine(FindRepositoryRoot(), "src", "bin", buildConfiguration, targetFramework, executableName);

            if (!File.Exists(_repoTodoExecutable))
            {
                throw new FileNotFoundException("The built RepoTodo CLI executable was not found.", _repoTodoExecutable);
            }
        }

        public TestRepository CreateRepository(string name)
        {
            var path = Path.Combine(_root, name);
            Directory.CreateDirectory(path);
            Repository.Init(path);
            return new TestRepository(path, DatabasePath, _repoTodoExecutable);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                foreach (var directory in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(directory, FileAttributes.Normal);
                }

                Directory.Delete(_root, recursive: true);
            }
        }

        private static string FindRepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "RepoTodo.slnx")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException("Could not locate RepoTodo.slnx from the test output directory.");
        }
    }

    private sealed class TestRepository(string workingDirectory, string databasePath, string cliExecutablePath)
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

        public string CreateCommit()
        {
            const string fileName = "test-commit.txt";
            File.WriteAllText(Path.Combine(workingDirectory, fileName), Guid.NewGuid().ToString("N"));

            using var repository = new Repository(workingDirectory);
            LibGit2Sharp.Commands.Stage(repository, fileName);
            var signature = new Signature("RepoTodo Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
            return repository.Commit("Test commit", signature, signature).Sha;
        }

        public async Task<CommandResult> RunAsync(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo(cliExecutablePath)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.Environment["REPOTODO_DB_PATH"] = databasePath;

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start RepoTodo CLI process.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync().WaitAsync(CommandTimeout);
            }
            catch (TimeoutException exception)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The process exited between the timeout and the kill attempt.
                }

                await process.WaitForExitAsync();

                var timedOutOutput = await standardOutput + await standardError;
                var command = string.Join(" ", arguments);
                throw new TimeoutException(
                    $"RepoTodo command timed out after {CommandTimeout.TotalSeconds:0} seconds: {command}{Environment.NewLine}{timedOutOutput}",
                    exception);
            }

            return new CommandResult(
                process.ExitCode,
                await standardOutput + await standardError);
        }
    }

    private sealed record CommandResult(int ExitCode, string Output);
}
