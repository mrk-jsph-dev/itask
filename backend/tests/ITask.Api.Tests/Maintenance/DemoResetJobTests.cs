using ITask.Api.Domain;
using ITask.Api.Infrastructure.Demo;
using ITask.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Tests.Maintenance;

public sealed class DemoResetJobTests(DemoApiFactory factory) : IClassFixture<DemoApiFactory>
{
    private const string DemoBoard = "Website relaunch";

    private static int CardsOf(string board) => DemoBoards.Data.Boards.Single(b => b.Name == board).Cards.Count;

    [Fact]
    public async Task Demo_workspace_has_every_board_with_its_cards_labels_and_checklists()
    {
        await factory.WithDbAsync(async db =>
        {
            var boards = await db.Boards.Include(b => b.Cards).ThenInclude(c => c.Checklist).Include(b => b.Labels)
                .Where(b => b.Members.Any(m => m.User!.IsDemo))
                .ToListAsync();
            Assert.Equal(DemoBoards.Data.Boards.Count, boards.Count);

            var board = boards.Single(b => b.Name == DemoBoard);
            Assert.Equal(CardsOf(DemoBoard), board.Cards.Count);
            Assert.Equal(CardsOf(DemoBoard) + 1, board.NextCardNumber);
            Assert.Equal(5, board.Labels.Count);
            Assert.Contains(board.Cards, c => c.Checklist.Count > 0);
            Assert.Contains(board.Cards, c => c.DueAt < DateTimeOffset.UtcNow && c.CompletedAt == null);
            Assert.Single(boards, b => b.ArchivedAt != null);
        });
    }

    [Fact]
    public async Task Demo_workspace_has_history_teammates_and_repeats()
    {
        var ownerEmail = DemoAccounts.Find("owner")!.Email;
        await factory.WithDbAsync(async db =>
        {
            var owner = await db.Users.SingleAsync(u => u.Email == ownerEmail);
            Assert.True(await db.Notifications.AnyAsync(n => n.UserId == owner.Id && n.ReadAt == null));
            Assert.True(await db.Comments.AnyAsync(c => c.Mentions.Any(m => m.UserId == owner.Id)));
            Assert.True(await db.Activity.CountAsync() > 100);
            Assert.Equal(4, await db.CardRecurrences.CountAsync());
            Assert.Equal(3, await db.AutomationRules.CountAsync());
            Assert.Equal(0, await db.AutomationRuns.CountAsync());
            Assert.Equal(DemoBoards.People.Count, await db.Users.CountAsync(u => u.IsDemo && u.PasswordHash == null));
        });
    }

    [Fact]
    public async Task Reset_removes_demo_changes_and_reseeds()
    {
        var demo = factory.CreateHttpsClient();
        (await demo.PostAsync(new Uri("/api/auth/demo/owner", UriKind.Relative), null)).EnsureSuccessStatusCode();
        var scratch = await demo.CreateBoardAsync("Scratch");

        var real = await factory.CreateSignedInClientAsync();
        var realBoard = await real.CreateBoardAsync("Real board");
        var editorEmail = DemoAccounts.Find("editor")!.Email;

        await factory.WithDbAsync(async db =>
        {
            var editor = await db.Users.SingleAsync(u => u.Email == editorEmail);
            db.BoardMembers.Add(new BoardMember { BoardId = realBoard, UserId = editor.Id, Role = BoardRole.Editor });
            var sample = await db.Cards.FirstAsync(c => c.Board!.Name == DemoBoard);
            sample.Title = "Changed by a visitor";
            await db.SaveChangesAsync();
        });

        await factory.RunJobAsync("demo-reset", DateTimeOffset.UtcNow);

        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Boards.AnyAsync(b => b.Id == scratch));
            Assert.True(await db.Boards.AnyAsync(b => b.Id == realBoard));
            Assert.Equal(1, await db.BoardMembers.CountAsync(m => m.BoardId == realBoard));

            var board = await db.Boards.Include(b => b.Members).Include(b => b.Cards).SingleAsync(b => b.Name == DemoBoard);
            Assert.Equal(5, board.Members.Count);
            Assert.Equal(CardsOf(DemoBoard), board.Cards.Count);
            Assert.DoesNotContain(board.Cards, c => c.Title == "Changed by a visitor");
            Assert.Equal(DemoBoards.Data.Boards.Count, await db.Boards.CountAsync(b => b.Members.All(m => m.User!.IsDemo)));
        });
    }
}
