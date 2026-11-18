using ITask.Api.Domain;
using ITask.Api.Infrastructure.Storage;
using ITask.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ITask.Api.Tests.Maintenance;

public sealed class CleanupJobTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Activity_older_than_retention_is_deleted()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var oldBoard = await owner.CreateBoardAsync("Old board");
        var newBoard = await owner.CreateBoardAsync("New board");
        var now = DateTimeOffset.UtcNow;

        await factory.WithDbAsync(async db =>
        {
            foreach (var entry in await db.Activity.Where(a => a.BoardId == oldBoard).ToListAsync())
            {
                entry.CreatedAt = now.AddDays(-181);
            }

            await db.SaveChangesAsync();
        });

        await factory.RunJobAsync("activity-retention", now);

        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Activity.AnyAsync(a => a.BoardId == oldBoard));
            Assert.True(await db.Activity.AnyAsync(a => a.BoardId == newBoard));
        });
    }

    [Fact]
    public async Task Blobs_without_an_attachment_are_deleted()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var storage = factory.Services.GetRequiredService<LocalBlobStorage>();
        var kept = $"boards/{card.BoardId:N}/cards/{card.CardId:N}/{Guid.NewGuid():N}";
        var orphan = $"boards/{card.BoardId:N}/cards/{card.CardId:N}/{Guid.NewGuid():N}";
        foreach (var name in new[] { kept, orphan })
        {
            using var content = new MemoryStream([1, 2, 3]);
            Assert.True(await storage.WriteAsync(name, content, "text/plain", CancellationToken.None));
        }

        var ownerId = await card.Board.Owner.GetUserIdAsync();
        await factory.WithDbAsync(async db =>
        {
            db.Attachments.Add(new Attachment
            {
                CardId = card.CardId,
                FileName = "kept.txt",
                ContentType = "text/plain",
                SizeBytes = 3,
                BlobName = kept,
                UploadedById = ownerId,
            });
            await db.SaveChangesAsync();
        });

        // Two days on, both files are past the orphan age.
        await factory.RunJobAsync("orphan-uploads", DateTimeOffset.UtcNow.AddDays(2));

        Assert.NotNull(await storage.GetInfoAsync(kept, CancellationToken.None));
        Assert.Null(await storage.GetInfoAsync(orphan, CancellationToken.None));
    }

    [Fact]
    public async Task Fresh_uploads_are_kept()
    {
        var storage = factory.Services.GetRequiredService<LocalBlobStorage>();
        var pending = $"boards/{Guid.NewGuid():N}/cards/{Guid.NewGuid():N}/{Guid.NewGuid():N}";
        using (var content = new MemoryStream([1]))
        {
            await storage.WriteAsync(pending, content, "text/plain", CancellationToken.None);
        }

        await factory.RunJobAsync("orphan-uploads", DateTimeOffset.UtcNow);

        Assert.NotNull(await storage.GetInfoAsync(pending, CancellationToken.None));
    }
}
