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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(user =>
        {
            user.Property(u => u.DisplayName).HasMaxLength(100);
        });

        builder.Entity<Board>(board =>
        {
            board.Property(b => b.Name).HasMaxLength(120);
            board.Property(b => b.Emoji).HasMaxLength(16);
        });

        builder.Entity<BoardMember>(member =>
        {
            member.HasKey(m => new { m.BoardId, m.UserId });
            member.Property(m => m.Role).HasConversion<string>().HasMaxLength(16);
            member.HasOne(m => m.Board).WithMany(b => b.Members).HasForeignKey(m => m.BoardId);
            member.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId);
            member.HasIndex(m => m.UserId);
        });
    }
}
