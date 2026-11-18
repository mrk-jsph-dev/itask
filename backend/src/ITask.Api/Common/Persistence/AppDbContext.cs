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
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (Database.IsSqlite())
        {
            foreach (var entry in ChangeTracker.Entries<Board>())
            {
                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    entry.Entity.RowVersion = RandomNumberGenerator.GetBytes(8);
                }
            }
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
