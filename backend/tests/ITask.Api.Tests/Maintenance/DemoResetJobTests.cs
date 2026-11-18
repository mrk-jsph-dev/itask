using ITask.Api.Domain;
using ITask.Api.Infrastructure.Demo;
using ITask.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Tests.Maintenance;

public sealed class DemoResetJobTests(DemoApiFactory factory) : IClassFixture<DemoApiFactory>
{
    private const string DemoBoard = "🚀 Demo: Website relaunch";

    [Fact]
    public async Task Sample_board_has_cards_labels_and_a_checklist()
    {
        await factory.WithDbAsync(async db =>
        {
            var board = await db.Boards.Include(b => b.Cards).ThenInclude(c => c.Checklist).Include(b => b.Labels)
                .SingleAsync(b => b.Name == DemoBoard);
            Assert.Equal(6, board.Cards.Count);
            Assert.Equal(3, board.Labels.Count);
            Assert.Equal(7, board.NextCardNumber);
            Assert.Equal(3, board.Cards.Sum(c => c.Checklist.Count));
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
            Assert.Equal(3, board.Members.Count);
            Assert.Equal(6, board.Cards.Count);
            Assert.DoesNotContain(board.Cards, c => c.Title == "Changed by a visitor");
        });
    }
}
