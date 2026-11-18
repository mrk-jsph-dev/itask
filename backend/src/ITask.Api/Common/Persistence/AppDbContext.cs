using System.Security.Cryptography;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Board> Boards => Set<Board>();

    public DbSet<BoardMember> BoardMembers => Set<BoardMember>();

    public DbSet<BoardStar> BoardStars => Set<BoardStar>();

    public DbSet<BoardInvite> BoardInvites => Set<BoardInvite>();

    public DbSet<BoardList> Lists => Set<BoardList>();

    public DbSet<Card> Cards => Set<Card>();

    public DbSet<Label> Labels => Set<Label>();

    public DbSet<CardLabel> CardLabels => Set<CardLabel>();

    public DbSet<CardAssignee> CardAssignees => Set<CardAssignee>();

    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<CommentMention> CommentMentions => Set<CommentMention>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<ActivityEntry> Activity => Set<ActivityEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(user =>
        {
            user.Property(u => u.DisplayName).HasMaxLength(100);
            user.Property(u => u.AvatarColor).HasMaxLength(7);
            user.Property(u => u.TimeZone).HasMaxLength(64);
        });

        builder.Entity<Board>(board =>
        {
            board.Property(b => b.Name).HasMaxLength(120);
            board.Property(b => b.Emoji).HasMaxLength(16);
            board.Property(b => b.Description).HasMaxLength(500);
            board.Property(b => b.Color).HasMaxLength(7);

            // SQLite has no rowversion type; the token is stamped in SaveChangesAsync instead.
            if (Database.IsSqlite())
            {
                board.Property(b => b.RowVersion).ValueGeneratedNever();
            }
        });

        builder.Entity<BoardMember>(member =>
        {
            member.HasKey(m => new { m.BoardId, m.UserId });
            member.Property(m => m.Role).HasConversion<string>().HasMaxLength(16);
            member.HasOne(m => m.Board).WithMany(b => b.Members).HasForeignKey(m => m.BoardId);
            member.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId);
            member.HasIndex(m => m.UserId);
        });

        builder.Entity<BoardStar>(star =>
        {
            star.HasKey(s => new { s.BoardId, s.UserId });
            star.HasOne(s => s.Board).WithMany(b => b.Stars).HasForeignKey(s => s.BoardId);
            star.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId);
            star.HasIndex(s => s.UserId);
        });

        builder.Entity<BoardInvite>(invite =>
        {
            invite.Property(i => i.Email).HasMaxLength(256);
            invite.Property(i => i.Role).HasConversion<string>().HasMaxLength(16);
            invite.Property(i => i.TokenHash).HasMaxLength(64);
            invite.HasIndex(i => i.TokenHash).IsUnique();

            // Accepting is a conditional update, so two concurrent accepts can't both succeed.
            invite.Property(i => i.AcceptedAt).IsConcurrencyToken();
            invite.HasOne(i => i.Board).WithMany(b => b.Invites).HasForeignKey(i => i.BoardId);
            invite.HasOne<AppUser>().WithMany().HasForeignKey(i => i.CreatedById).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<BoardList>(list =>
        {
            list.ToTable("Lists");
            list.Property(l => l.Name).HasMaxLength(BoardList.NameMaxLength);
            list.Property(l => l.Emoji).HasMaxLength(16);
            list.HasOne(l => l.Board).WithMany(b => b.Lists).HasForeignKey(l => l.BoardId);
            list.HasIndex(l => new { l.BoardId, l.Position });

            // Archived lists keep their name, so only active lists must be unique.
            list.HasIndex(l => new { l.BoardId, l.Name }).IsUnique().HasFilter("[ArchivedAt] IS NULL");

            if (Database.IsSqlite())
            {
                list.Property(l => l.RowVersion).ValueGeneratedNever();
            }
        });

        builder.Entity<Card>(card =>
        {
            card.Property(c => c.Title).HasMaxLength(Card.TitleMaxLength);
            card.Property(c => c.Description).HasMaxLength(Card.DescriptionMaxLength);
            card.Property(c => c.CoverColor).HasMaxLength(7);
            card.Property(c => c.Priority).HasConversion<string>().HasMaxLength(16);
            card.HasOne(c => c.Board).WithMany(b => b.Cards).HasForeignKey(c => c.BoardId);

            // Boards cascade to cards already; a second cascade path through lists is not allowed on SQL Server.
            card.HasOne(c => c.List).WithMany().HasForeignKey(c => c.ListId).OnDelete(DeleteBehavior.NoAction);
            card.HasOne<AppUser>().WithMany().HasForeignKey(c => c.CreatedById).OnDelete(DeleteBehavior.NoAction);
            card.HasIndex(c => new { c.ListId, c.Position });
            card.HasIndex(c => new { c.BoardId, c.Number }).IsUnique();

            if (Database.IsSqlite())
            {
                card.Property(c => c.RowVersion).ValueGeneratedNever();
            }
        });

        builder.Entity<Label>(label =>
        {
            label.Property(l => l.Name).HasMaxLength(Label.NameMaxLength);
            label.Property(l => l.Color).HasMaxLength(7);
            label.Property(l => l.Emoji).HasMaxLength(16);
            label.HasOne(l => l.Board).WithMany(b => b.Labels).HasForeignKey(l => l.BoardId);
            label.HasIndex(l => new { l.BoardId, l.Name }).IsUnique();
        });

        builder.Entity<CardLabel>(cardLabel =>
        {
            cardLabel.HasKey(cl => new { cl.CardId, cl.LabelId });
            cardLabel.HasOne(cl => cl.Card).WithMany(c => c.Labels).HasForeignKey(cl => cl.CardId);

            // Deleting a board cascades through cards; label deletes clear their links in the slice.
            cardLabel.HasOne(cl => cl.Label).WithMany().HasForeignKey(cl => cl.LabelId).OnDelete(DeleteBehavior.NoAction);
            cardLabel.HasIndex(cl => cl.LabelId);
        });

        builder.Entity<CardAssignee>(assignee =>
        {
            assignee.HasKey(a => new { a.CardId, a.UserId });
            assignee.HasOne(a => a.Card).WithMany(c => c.Assignees).HasForeignKey(a => a.CardId);
            assignee.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.NoAction);
            assignee.HasIndex(a => a.UserId);
        });

        builder.Entity<ChecklistItem>(item =>
        {
            item.Property(i => i.Text).HasMaxLength(ChecklistItem.TextMaxLength);
            item.HasOne(i => i.Card).WithMany(c => c.Checklist).HasForeignKey(i => i.CardId);
            item.HasOne(i => i.Assignee).WithMany().HasForeignKey(i => i.AssigneeId).OnDelete(DeleteBehavior.NoAction);
            item.HasIndex(i => new { i.CardId, i.Position });
            item.HasIndex(i => i.AssigneeId);
        });

        builder.Entity<Comment>(comment =>
        {
            comment.Property(c => c.Body).HasMaxLength(Comment.BodyMaxLength);
            comment.HasOne(c => c.Card).WithMany(c => c.Comments).HasForeignKey(c => c.CardId);
            comment.HasOne(c => c.Author).WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.NoAction);
            comment.HasIndex(c => new { c.CardId, c.CreatedAt });
            comment.HasIndex(c => c.AuthorId);

            // SQLite can't order by DateTimeOffset; UTC ticks keep comment paging in the database.
            if (Database.IsSqlite())
            {
                comment.Property(c => c.CreatedAt).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
            }
        });

        builder.Entity<CommentMention>(mention =>
        {
            mention.HasKey(m => new { m.CommentId, m.UserId });
            mention.HasOne(m => m.Comment).WithMany(c => c.Mentions).HasForeignKey(m => m.CommentId);
            mention.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.NoAction);
            mention.HasIndex(m => m.UserId);
        });

        builder.Entity<Attachment>(attachment =>
        {
            attachment.Property(a => a.FileName).HasMaxLength(Attachment.FileNameMaxLength);
            attachment.Property(a => a.ContentType).HasMaxLength(Attachment.ContentTypeMaxLength);
            attachment.Property(a => a.BlobName).HasMaxLength(Attachment.BlobNameMaxLength);
            attachment.HasOne(a => a.Card).WithMany(c => c.Attachments).HasForeignKey(a => a.CardId);
            attachment.HasOne(a => a.UploadedBy).WithMany().HasForeignKey(a => a.UploadedById).OnDelete(DeleteBehavior.NoAction);
            attachment.HasIndex(a => a.CardId);
            attachment.HasIndex(a => a.BlobName).IsUnique();
            attachment.HasIndex(a => a.UploadedById);
        });

        builder.Entity<ActivityEntry>(entry =>
        {
            entry.ToTable("Activity");
            entry.Property(a => a.Type).HasMaxLength(ActivityEntry.TypeMaxLength);
            entry.HasOne(a => a.Board).WithMany().HasForeignKey(a => a.BoardId);
            entry.HasOne(a => a.Actor).WithMany().HasForeignKey(a => a.ActorId).OnDelete(DeleteBehavior.NoAction);
            entry.HasIndex(a => new { a.BoardId, a.CreatedAt });
            entry.HasIndex(a => new { a.CardId, a.CreatedAt });
            entry.HasIndex(a => new { a.ActorId, a.CreatedAt });

            // Same as comments: SQLite orders the feeds by UTC ticks.
            if (Database.IsSqlite())
            {
                entry.Property(a => a.CreatedAt).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
            }
        });

        builder.Entity<Board>().Property(b => b.KeyPrefix).HasMaxLength(CardKeys.MaxLength);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (Database.IsSqlite())
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State is EntityState.Added or EntityState.Modified
                    && entry.Metadata.FindProperty(nameof(Board.RowVersion)) is { IsConcurrencyToken: true } rowVersion)
                {
                    entry.Property(rowVersion.Name).CurrentValue = RandomNumberGenerator.GetBytes(8);
                }
            }
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
