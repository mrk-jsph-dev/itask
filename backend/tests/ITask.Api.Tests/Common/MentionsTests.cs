using ITask.Api.Common.Cards;

namespace ITask.Api.Tests.Common;

public sealed class MentionsTests
{
    private static readonly Guid Ada = Guid.NewGuid();
    private static readonly Guid AdaL = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static readonly (Guid, string)[] Members = [(Ada, "Ada"), (AdaL, "Ada Lovelace"), (Bob, "Bob")];

    [Theory]
    [InlineData("@Ada Lovelace can you look?", "AdaL")]
    [InlineData("@ada, then @BOB.", "Ada,Bob")]
    [InlineData("@Adam is not a member", "")]
    [InlineData("mail bob@Bob.com", "")]
    [InlineData("@Bob @Bob", "Bob")]
    [InlineData("no mentions", "")]
    public void Parse_matches_whole_names(string body, string expected)
    {
        var names = new Dictionary<Guid, string> { [Ada] = "Ada", [AdaL] = "AdaL", [Bob] = "Bob" };

        var found = Mentions.Parse(body, Members).Select(id => names[id]);

        Assert.Equal(expected, string.Join(',', found));
    }
}
