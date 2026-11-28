using ITask.Api.Common.Notifications;
using ITask.Api.Infrastructure.Notifications;

namespace ITask.Api.Tests.Common;

public sealed class DigestTextTests
{
    [Fact]
    public void Unblocked_names_the_card_and_board()
    {
        var text = DigestText.Describe(
            NotificationTypes.Unblocked,
            """{"blocker":{"key":"ITK-2","title":"Write tests"},"card":{"key":"ITK-1","title":"Ship release"}}""",
            "Ana",
            "Roadmap",
            TimeZoneInfo.Utc);

        Assert.Equal("ITK-1 Ship release is no longer blocked · Roadmap", text);
    }
}
