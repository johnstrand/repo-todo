using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RepoTodo.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubIssueSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TodoGitHubSyncStates",
                columns: table => new
                {
                    TodoGitHubSyncStateId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepoId = table.Column<int>(type: "INTEGER", nullable: false),
                    TodoItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    GitHubIssueNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSyncedSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoGitHubSyncStates", x => x.TodoGitHubSyncStateId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TodoGitHubSyncStates_RepoId_GitHubIssueNumber",
                table: "TodoGitHubSyncStates",
                columns: new[] { "RepoId", "GitHubIssueNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TodoGitHubSyncStates_RepoId_TodoItemId",
                table: "TodoGitHubSyncStates",
                columns: new[] { "RepoId", "TodoItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TodoGitHubSyncStates");
        }
    }
}
