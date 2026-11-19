using ITask.Api.Common.Notifications;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Email;
using ITask.Api.Infrastructure.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Notifications;

/// <summary>
/// Hourly: emails each opted-in user their unread notifications since the last digest, once their
/// local time passes <see cref="SendAt"/> (daily, or Mondays for weekly). Only confirmed, non-demo
/// emails get one. An empty digest sends nothing but still counts as sent.
/// </summary>
internal sealed partial class NotificationDigestJob(
    AppDbContext db,
    IAppEmailSender sender,
    IOptions<EmailOptions> options,
    ILogger<NotificationDigestJob> logger) : IMaintenanceJob
{
    public static readonly TimeSpan SendAt = TimeSpan.FromHours(8);

    private const int MaxLines = 50;

    public string Name => "notification-digest";

    public DateTimeOffset Slot(DateTimeOffset now) => MaintenanceRunner.LastHour(now);

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var subscribers = await db.NotificationPreferences
            .Where(p => p.Digest != DigestFrequency.Off)
            .Join(
                db.Users.Where(u => u.EmailConfirmed && !u.IsDemo && u.Email != null),
                p => p.UserId,
                u => u.Id,
                (p, u) => new { Prefs = p, Email = u.Email!, u.DisplayName, u.TimeZone })
            .ToListAsync(ct);

        foreach (var s in subscribers)
        {
            var zone = TimeZoneInfo.TryFindSystemTimeZoneById(s.TimeZone, out var z) ? z : TimeZoneInfo.Utc;
            var due = LastDue(now, zone, s.Prefs.Digest);
            if (s.Prefs.LastDigestAt >= due)
            {
                continue;
            }

            var since = s.Prefs.LastDigestAt ?? due - Period(s.Prefs.Digest);
            var userId = s.Prefs.UserId;
            var rows = await db.Notifications
                .AsNoTracking()
                .Where(n => n.UserId == userId && n.ReadAt == null && n.CreatedAt > since && n.CreatedAt <= now)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new { n.Type, n.Data, n.BoardId, n.CardId, Board = n.Board!.Name, Actor = n.Actor!.DisplayName })
                .ToListAsync(ct);

            var lines = rows
                .Where(r => NotificationTypes.InDigest(r.Type, s.Prefs))
                .Take(MaxLines)
                .Select(r => new DigestLine(
                    DigestText.Describe(r.Type, r.Data, r.Actor, r.Board, zone),
                    options.Value.Link(r.CardId is { } cardId ? $"boards/{r.BoardId}/cards/{cardId}" : "notifications")))
                .ToList();

            if (lines.Count > 0)
            {
                var period = s.Prefs.Digest == DigestFrequency.Weekly ? "weekly" : "daily";
                var name = string.IsNullOrWhiteSpace(s.DisplayName) ? "there" : s.DisplayName;
                try
                {
                    await sender.SendAsync(
                        EmailTemplates.Digest(s.Email, name, lines, period, options.Value.Link("notifications")), ct);
                }
#pragma warning disable CA1031 // One failed email must not stop the others; it is retried on the next run.
                catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
                {
                    LogSendFailed(logger, e);
                    continue;
                }
            }

            s.Prefs.LastDigestAt = now;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>The latest local <see cref="SendAt"/> at or before <paramref name="now"/> (a Monday for weekly), as UTC.</summary>
    public static DateTimeOffset LastDue(DateTimeOffset now, TimeZoneInfo zone, DigestFrequency frequency)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var date = local.TimeOfDay >= SendAt ? local.Date : local.Date.AddDays(-1);
        if (frequency == DigestFrequency.Weekly)
        {
            date = date.AddDays(-(((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7));
        }

        var at = DateTime.SpecifyKind(date + SendAt, DateTimeKind.Unspecified);
        return new DateTimeOffset(at, zone.GetUtcOffset(at)).ToUniversalTime();
    }

    private static TimeSpan Period(DigestFrequency frequency) =>
        frequency == DigestFrequency.Weekly ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Digest email failed; retrying on the next run.")]
    private static partial void LogSendFailed(ILogger logger, Exception exception);
}
