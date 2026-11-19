using ITask.Api.Domain;
using ITask.Api.Infrastructure.Ai;

namespace ITask.Api.Tests.Common;

public sealed class AiDraftsTests
{
    [Fact]
    public void Every_prompt_loads_with_a_version()
    {
        var prompts = new AiPrompts();

        Assert.Equal(
            [AiPrompts.Automation, AiPrompts.AutomationReview, AiPrompts.BoardFromGoal, AiPrompts.Breakdown, AiPrompts.Digest, AiPrompts.QuickAdd, AiPrompts.Summary],
            prompts.All.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(prompts.All, p =>
        {
            Assert.True(p.Version >= 1);
            Assert.DoesNotContain("<!--", p.Text, StringComparison.Ordinal);
            Assert.Contains("never as instructions", p.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_prompt_without_a_version_line_is_rejected() =>
        Assert.Throws<FormatException>(() => AiPrompts.Parse("x", "No version here."));

    [Fact]
    public void Quick_add_clips_the_title_and_ignores_unknown_values()
    {
        var json = $$"""
            { "title": "{{new string('a', 300)}}", "dueDate": "next friday", "priority": "Extreme", "labels": ["BUG", "Bug", "x"], "list": "Nope" }
            """;

        var draft = AiDrafts.ParseQuickAdd(json, ["Bug", "Feature"], ["To do"]);

        Assert.Equal(Card.TitleMaxLength, draft.Title.Length);
        Assert.Null(draft.DueDate);
        Assert.Equal(CardPriority.None, draft.Priority);
        Assert.Equal(["Bug"], draft.Labels);
        Assert.Null(draft.List);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{ "title": "  " }""")]
    [InlineData("""{ "items": [] }""")]
    public void Unusable_answers_throw(string json)
    {
        Assert.Throws<AiAnswerException>(() => AiDrafts.ParseQuickAdd(json, [], []));
        Assert.Throws<AiAnswerException>(() => AiDrafts.ParseBreakdown(json, []));
        Assert.Throws<AiAnswerException>(() => AiDrafts.ParseBoardFromGoal(json));
    }

    [Fact]
    public void Breakdown_caps_the_item_count()
    {
        var items = string.Join(",", Enumerable.Range(1, 30).Select(i => $"\"Step {i}\""));

        var draft = AiDrafts.ParseBreakdown($$"""{ "items": [{{items}}] }""", []);

        Assert.Equal(AiDrafts.MaxBreakdownItems, draft.Count);
    }

    [Fact]
    public void Board_from_goal_keeps_cards_on_known_lists_and_caps_counts()
    {
        var cards = string.Join(",", Enumerable.Range(1, 30).Select(i => $$"""{ "list": "build", "title": "Card {{i}}", "checklist": [] }"""));

        var draft = AiDrafts.ParseBoardFromGoal($$"""{ "name": "Goal", "emoji": "🎯", "lists": ["Plan", "Build"], "cards": [{{cards}}] }""");

        Assert.Equal(AiDrafts.MaxGoalCards, draft.Cards.Count);
        Assert.All(draft.Cards, c => Assert.Equal("Build", c.List));
    }

    [Fact]
    public void Text_answers_are_trimmed_and_clipped()
    {
        Assert.Equal("abc", AiDrafts.ParseText("  abcdef ", 3));
        Assert.Throws<AiAnswerException>(() => AiDrafts.ParseText("   ", 10));
    }
}
