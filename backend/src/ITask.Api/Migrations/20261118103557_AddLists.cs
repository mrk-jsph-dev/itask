using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITask.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Lists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Emoji = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Position = table.Column<double>(type: "float", nullable: false),
                    WipLimit = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lists_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Lists_BoardId_Name",
                table: "Lists",
                columns: new[] { "BoardId", "Name" },
                unique: true,
                filter: "[ArchivedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Lists_BoardId_Position",
                table: "Lists",
                columns: new[] { "BoardId", "Position" });

            // Boards created before lists existed get the same default lists as new boards.
            migrationBuilder.Sql(
                """
                INSERT INTO [Lists] ([Id], [BoardId], [Name], [Emoji], [Position], [CreatedAt])
                SELECT NEWID(), b.[Id], d.[Name], d.[Emoji], d.[Position], SYSDATETIMEOFFSET()
                FROM [Boards] b
                CROSS JOIN (VALUES
                    (N'To do', N'📝', 1024.0),
                    (N'In progress', N'🚧', 2048.0),
                    (N'Review', N'👀', 3072.0),
                    (N'Done', N'✅', 4096.0)) AS d([Name], [Emoji], [Position]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Lists");
        }
    }
}
