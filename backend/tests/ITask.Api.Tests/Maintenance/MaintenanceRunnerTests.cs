using ITask.Api.Infrastructure.Maintenance;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Maintenance;

public sealed class MaintenanceRunnerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] DailyJobs = ["activity-retention", "demo-reset", "notification-retention", "orphan-uploads", "unverified-accounts"];

    private static readonly string[] HourlyJobs = ["due-reminders", "notification-digest", "recurring-cards"];

    [Fact]
    public async Task Each_job_runs_once_per_slot()
    {
        var first = await factory.RunDueAsync(new DateTimeOffset(2030, 1, 1, 4, 0, 0, TimeSpan.Zero));
        var sameHour = await factory.RunDueAsync(new DateTimeOffset(2030, 1, 1, 4, 59, 0, TimeSpan.Zero));
        var sameDay = await factory.RunDueAsync(new DateTimeOffset(2030, 1, 2, 2, 59, 0, TimeSpan.Zero));
        var nextSlot = await factory.RunDueAsync(new DateTimeOffset(2030, 1, 2, 3, 0, 0, TimeSpan.Zero));

        Assert.Equal(DailyJobs.Concat(HourlyJobs).Order(), first.Order());
        Assert.Empty(sameHour);
        Assert.Equal(HourlyJobs, sameDay.Order());
        Assert.Equal(DailyJobs.Concat(HourlyJobs).Order(), nextSlot.Order());
    }

    [Theory]
    [InlineData("2030-01-01T04:59:59Z", "2030-01-01T04:00:00Z")]
    [InlineData("2030-01-01T05:30:00+02:00", "2030-01-01T03:00:00Z")]
    public void Last_hour_is_the_start_of_the_utc_hour(string now, string expected) =>
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            MaintenanceRunner.LastHour(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("2030-01-01T02:59:00Z", "2029-12-31T03:00:00Z")]
    [InlineData("2030-01-01T03:00:00Z", "2030-01-01T03:00:00Z")]
    [InlineData("2030-01-01T23:00:00+05:00", "2030-01-01T03:00:00Z")]
    public void Last_slot_is_the_latest_0300_utc(string now, string expected) =>
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            MaintenanceRunner.LastSlot(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));
}
