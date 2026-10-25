using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RepoTodo.Migrations
{
    /// <inheritdoc />
    public partial class FixRepoNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TodoItems_Remotes_RepoId",
                table: "TodoItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Remotes",
                table: "Remotes");

            migrationBuilder.DropColumn(
                name: "RemoteId",
                table: "TodoItems");

            migrationBuilder.RenameTable(
                name: "Remotes",
                newName: "Repos");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Repos",
                table: "Repos",
                column: "RepoId");

            migrationBuilder.AddForeignKey(
                name: "FK_TodoItems_Repos_RepoId",
                table: "TodoItems",
                column: "RepoId",
                principalTable: "Repos",
                principalColumn: "RepoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TodoItems_Repos_RepoId",
                table: "TodoItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Repos",
                table: "Repos");

            migrationBuilder.RenameTable(
                name: "Repos",
                newName: "Remotes");

            migrationBuilder.AddColumn<int>(
                name: "RemoteId",
                table: "TodoItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Remotes",
                table: "Remotes",
                column: "RepoId");

            migrationBuilder.AddForeignKey(
                name: "FK_TodoItems_Remotes_RepoId",
                table: "TodoItems",
                column: "RepoId",
                principalTable: "Remotes",
                principalColumn: "RepoId");
        }
    }
}
