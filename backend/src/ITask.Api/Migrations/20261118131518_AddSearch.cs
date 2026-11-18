using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITask.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchBody",
                table: "Comments",
                type: "nvarchar(max)",
                maxLength: 5000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchTitle",
                table: "Cards",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchName",
                table: "Boards",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE [Boards] SET [SearchName] = LOWER([Name]);");
            migrationBuilder.Sql("UPDATE [Cards] SET [SearchTitle] = LOWER([Title]);");
            migrationBuilder.Sql("UPDATE [Comments] SET [SearchBody] = LOWER([Body]);");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_BoardId_SearchTitle",
                table: "Cards",
                columns: new[] { "BoardId", "SearchTitle" });

            migrationBuilder.CreateIndex(
                name: "IX_Boards_SearchName",
                table: "Boards",
                column: "SearchName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cards_BoardId_SearchTitle",
                table: "Cards");

            migrationBuilder.DropIndex(
                name: "IX_Boards_SearchName",
                table: "Boards");

            migrationBuilder.DropColumn(
                name: "SearchBody",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "SearchTitle",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "SearchName",
                table: "Boards");
        }
    }
}
