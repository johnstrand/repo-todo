using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RepoTodo.Migrations
{
    /// <inheritdoc />
    public partial class Init2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TodoItems_Remotes_RemoteId",
                table: "TodoItems");

            migrationBuilder.DropIndex(
                name: "IX_TodoItems_RemoteId",
                table: "TodoItems");

            migrationBuilder.AddColumn<int>(
                name: "RepoId",
                table: "TodoItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_RepoId",
                table: "TodoItems",
                column: "RepoId");

            migrationBuilder.AddForeignKey(
                name: "FK_TodoItems_Remotes_RepoId",
                table: "TodoItems",
                column: "RepoId",
                principalTable: "Remotes",
                principalColumn: "RepoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TodoItems_Remotes_RepoId",
                table: "TodoItems");

            migrationBuilder.DropIndex(
                name: "IX_TodoItems_RepoId",
                table: "TodoItems");

            migrationBuilder.DropColumn(
                name: "RepoId",
                table: "TodoItems");

            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_RemoteId",
                table: "TodoItems",
                column: "RemoteId");

            migrationBuilder.AddForeignKey(
                name: "FK_TodoItems_Remotes_RemoteId",
                table: "TodoItems",
                column: "RemoteId",
                principalTable: "Remotes",
                principalColumn: "RepoId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
