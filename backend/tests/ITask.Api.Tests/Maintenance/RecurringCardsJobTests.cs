using System.Net.Http.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Maintenance;

public sealed class RecurringCardsJobTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<RecurrenceView> WeeklyAsync(CardOnBoard card, params int[] weekdays)
    {
        var todo = (await card.Board.Owner.GetListsAsync(card.BoardId))[0].Id;
        var response = await card.Board.Owner.PutAsJsonAsync($"{card.Url}/recurrence", new { kind = "Weekly", weekdays, targetListId = todo });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecurrenceView>())!;
    }

    private static async Task<List<CardSummary>> CardsAsync(CardOnBoard card) =>
        [.. (await card.Board.Owner.GetListsAsync(card.BoardId)).SelectMany(l => l.Cards).OrderBy(c => c.Number)];

    [Fact]
    public async Task A_weekly_card_regenerates_at_its_next_run_once()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var rule = await WeeklyAsync(card, (int)DayOfWeek.Monday);
        Assert.Equal(DayOfWeek.Monday, rule.NextRunAt.UtcDateTime.DayOfWeek);

        await factory.RunJobAsync("recurring-cards", rule.NextRunAt.AddMinutes(-1));
        Assert.Single(await CardsAsync(card));

        await factory.RunJobAsync("recurring-cards", rule.NextRunAt.AddHours(1));
        await factory.RunJobAsync("recurring-cards", rule.NextRunAt.AddHours(2));
        var cards = await CardsAsync(card);
        Assert.Equal(2, cards.Count);
        Assert.Equal((false, true), (cards[0].Recurring, cards[1].Recurring));
        Assert.Null(cards[0].CompletedAt);

        // A week later the copy repeats in turn.
        await factory.RunJobAsync("recurring-cards", rule.NextRunAt.AddDays(7).AddHours(1));
        Assert.Equal(3, (await CardsAsync(card)).Count);
    }

    [Fact]
    public async Task A_long_pause_makes_one_copy_for_the_latest_occurrence()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var due = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
        (await card.Board.Owner.PatchAsJsonAsync(card.Url, new { dueAt = due })).EnsureSuccessStatusCode();
        var rule = await WeeklyAsync(card, (int)DayOfWeek.Monday);

        await factory.RunJobAsync("recurring-cards", rule.NextRunAt.AddDays(22));

        var copy = (await CardsAsync(card))[^1];
        Assert.Equal(2, copy.Number);
        Assert.Equal(rule.NextRunAt.AddDays(21).AddHours(9), copy.DueAt);
    }

    [Fact]
    public async Task Archived_cards_and_boards_wait()
    {
        var archivedCard = await CardOnBoard.CreateAsync(factory);
        var cardRule = await WeeklyAsync(archivedCard, 1, 4);
        await archivedCard.Board.Owner.PostEmptyAsync($"{archivedCard.Url}/archive");
        var archivedBoard = await CardOnBoard.CreateAsync(factory);
        var boardRule = await WeeklyAsync(archivedBoard, 1, 4);
        await archivedBoard.ArchiveBoardAsync();

        await factory.RunJobAsync("recurring-cards", cardRule.NextRunAt.AddDays(1));
        await factory.RunJobAsync("recurring-cards", boardRule.NextRunAt.AddDays(1));

        await archivedCard.Board.Owner.PostEmptyAsync($"{archivedCard.Url}/restore");
        Assert.Single(await CardsAsync(archivedCard));
        await archivedBoard.Board.Owner.PostEmptyAsync($"/api/boards/{archivedBoard.BoardId}/restore");
        Assert.Single(await CardsAsync(archivedBoard));
    }
}
