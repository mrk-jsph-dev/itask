using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITask.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsageCounters",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageCounters", x => new { x.Name, x.Date });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsageCounters");
        }
    }
}
