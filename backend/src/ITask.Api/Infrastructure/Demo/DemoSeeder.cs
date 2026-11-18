using ITask.Api.Common.Ordering;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Demo;

/// <summary>
/// Idempotently creates the <see cref="DemoAccounts"/> (Platform admin, board Owner,
/// Editor, Viewer) and a shared sample board. Runs only when <c>Demo:Enabled</c> is true and <c>Demo:Password</c> is set.
/// </summary>
public sealed partial class DemoSeeder(
    AppDbContext db,
    UserManager<AppUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IOptions<DemoOptions> options,
    ILogger<DemoSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.Password))
        {
            LogMissingPassword(logger);
            return;
        }

        if (!await roles.RoleExistsAsync(PlatformRoles.PlatformAdmin))
        {
            await roles.CreateAsync(new IdentityRole<Guid>(PlatformRoles.PlatformAdmin));
        }

        const string boardName = "🚀 Demo: Website relaunch";
        var board = await db.Boards.Include(b => b.Members).FirstOrDefaultAsync(b => b.Name == boardName, ct);
        var isNewBoard = board is null;
        if (board is null)
        {
            board = new Board
            {
                Name = boardName,
                KeyPrefix = "WEB",
                Emoji = "🚀",
                Description = "Sample board shared by the demo accounts.",
                Color = "#0F766E",
            };
            board.Lists.AddRange(BoardList.CreateDefaults());
            db.Boards.Add(board);
        }

        Guid? ownerId = null;
        foreach (var (_, email, name, role, admin) in DemoAccounts.All)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = name, IsDemo = true };
                var created = await users.CreateAsync(user, settings.Password);
                if (!created.Succeeded)
                {
                    LogSeedFailed(logger, email, string.Join("; ", created.Errors.Select(e => e.Code)));
                    continue;
                }
            }

            if (admin && !await users.IsInRoleAsync(user, PlatformRoles.PlatformAdmin))
            {
                await users.AddToRoleAsync(user, PlatformRoles.PlatformAdmin);
            }

            if (role is { } boardRole && board.Members.All(m => m.UserId != user.Id))
            {
                board.Members.Add(new BoardMember { UserId = user.Id, Role = boardRole });
            }

            if (role == BoardRole.Owner)
            {
                ownerId = user.Id;
            }
        }

        if (isNewBoard && ownerId is { } creator)
        {
            AddSampleCards(db, board, creator);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Labels, cards and a checklist so the sample board shows each feature.</summary>
    /// <remarks>Added through the context: keys are set client-side, so EF would treat them as existing rows.</remarks>
    private static void AddSampleCards(AppDbContext db, Board board, Guid createdById)
    {
        var design = new Label { Name = "Design", Color = "#7C3AED", Emoji = "🎨" };
        var content = new Label { Name = "Content", Color = "#0EA5E9", Emoji = "✍️" };
        var bug = new Label { Name = "Bug", Color = "#DC2626", Emoji = "🐞" };
        foreach (var label in new[] { design, content, bug })
        {
            label.Board = board;
            db.Labels.Add(label);
        }

        (int List, string Title, CardPriority Priority, Label? Label)[] samples =
        [
            (0, "Collect feedback on the current site", CardPriority.Medium, content),
            (0, "Pick a font pairing", CardPriority.Low, design),
            (1, "Design the new home page", CardPriority.High, design),
            (1, "Fix broken links in the footer", CardPriority.Urgent, bug),
            (2, "Rewrite the About page", CardPriority.Medium, content),
            (3, "Set up analytics", CardPriority.None, null),
        ];

        var cards = new List<Card>();
        var positions = new Dictionary<int, double>();
        foreach (var (listIndex, title, priority, label) in samples)
        {
            var position = positions.GetValueOrDefault(listIndex) + PositionCalculator.Step;
            positions[listIndex] = position;

            var card = new Card
            {
                Board = board,
                List = board.Lists[listIndex],
                Number = board.NextCardNumber++,
                Title = title,
                Priority = priority,
                Position = position,
                CreatedById = createdById,
                CompletedAt = listIndex == board.Lists.Count - 1 ? DateTimeOffset.UtcNow : null,
            };

            if (label is not null)
            {
                card.Labels.Add(new CardLabel { Label = label });
            }

            cards.Add(card);
        }

        var home = cards[2];
        home.Description = "Hero, three feature tiles and a sign-up call to action.";
        home.Checklist.AddRange(
        [
            new ChecklistItem { Text = "Wireframe", IsDone = true, Position = PositionCalculator.Step },
            new ChecklistItem { Text = "High-fidelity mock-up", Position = PositionCalculator.Step * 2 },
            new ChecklistItem { Text = "Review with the team", Position = PositionCalculator.Step * 3 },
        ]);
        db.Cards.AddRange(cards);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo seeding is enabled but Demo:Password is not set; skipping.")]
    private static partial void LogMissingPassword(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not create demo account {Email}: {Errors}")]
    private static partial void LogSeedFailed(ILogger logger, string email, string errors);
}
