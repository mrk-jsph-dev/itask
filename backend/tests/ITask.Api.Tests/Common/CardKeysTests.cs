using ITask.Api.Domain;

namespace ITask.Api.Tests.Common;

public sealed class CardKeysTests
{
    [Theory]
    [InlineData("Website relaunch", "WR")]
    [InlineData("Roadmap", "ROA")]
    [InlineData("🚀 Demo: Website relaunch", "DWR")]
    [InlineData("a b c d e f g", "ABCDE")]
    [InlineData("Go", "GO")]
    [InlineData("X", "ITK")]
    [InlineData("Équipe été", "QT")]
    [InlineData("📋", "ITK")]
    public void Default_prefix_comes_from_the_name(string name, string expected) =>
        Assert.Equal(expected, CardKeys.DefaultPrefix(name));
}
