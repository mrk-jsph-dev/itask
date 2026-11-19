using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITask.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ListEnteredAt",
                table: "Cards",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Existing cards count as having arrived in their list when they were created.
            migrationBuilder.Sql("UPDATE Cards SET ListEnteredAt = CreatedAt;");

            migrationBuilder.CreateTable(
                name: "AutomationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ListId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Direction = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Trigger = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Conditions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Actions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationRules_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AutomationRules_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AutomationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TriggeredById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    UndoJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UndoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationRuns_AutomationRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "AutomationRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_BoardId",
                table: "AutomationRules",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_CreatedById",
                table: "AutomationRules",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRuns_BoardId_CreatedAt",
                table: "AutomationRuns",
                columns: new[] { "BoardId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRuns_CardId",
                table: "AutomationRuns",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRuns_RuleId_CardId_Key",
                table: "AutomationRuns",
                columns: new[] { "RuleId", "CardId", "Key" },
                unique: true,
                filter: "[Key] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationRuns");

            migrationBuilder.DropTable(
                name: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "ListEnteredAt",
                table: "Cards");
        }
    }
}
