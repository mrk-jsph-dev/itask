using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITask.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCardDependencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Unblocked",
                table: "NotificationPreferences",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "CardDependencies",
                columns: table => new
                {
                    CardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlockedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardDependencies", x => new { x.CardId, x.BlockedById });
                    table.ForeignKey(
                        name: "FK_CardDependencies_Cards_BlockedById",
                        column: x => x.BlockedById,
                        principalTable: "Cards",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CardDependencies_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardDependencies_BlockedById",
                table: "CardDependencies",
                column: "BlockedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardDependencies");

            migrationBuilder.DropColumn(
                name: "Unblocked",
                table: "NotificationPreferences");
        }
    }
}
