using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITask.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddThemePreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThemePreference",
                table: "AspNetUsers",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "System");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThemePreference",
                table: "AspNetUsers");
        }
    }
}
