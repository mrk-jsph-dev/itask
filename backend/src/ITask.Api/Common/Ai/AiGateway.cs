using ITask.Api.Common.Auth;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ITask.Api.Common.Ai;

/// <summary>Outcome of one AI call: the parsed draft, or the problem to return. <see cref="Remaining"/> is the caller's quota left today.</summary>
public sealed record AiOutcome<T>(T? Value, ProblemHttpResult? Problem, int Remaining);

/// <summary>
/// The only way slices call the model: consumes one request from the caller's daily quota
/// (429 when spent), calls <see cref="IAiService"/>, records token use and parses the answer
/// (502 when the model fails or answers with something unusable; the request still counts).
/// </summary>
public sealed partial class AiGateway(
    AppDbContext db,
    ICurrentUser user,
    IAiService ai,
    AiPrompts prompts,
    IOptions<AiOptions> options,
    TimeProvider time,
    ILogger<AiGateway> logger)
{
    public int DailyLimit => Math.Max(0, options.Value.DailyRequestsPerUser);

    public async Task<int> RemainingAsync(CancellationToken ct)
    {
        var today = Today();
        var used = await db.AiUsage
            .Where(u => u.UserId == user.Id && u.Date == today)
            .Select(u => (int?)u.Requests)
            .FirstOrDefaultAsync(ct) ?? 0;
        return Math.Max(0, DailyLimit - used);
    }

    public async Task<AiOutcome<T>> RunAsync<T>(
        string promptName,
        string userPrompt,
        int maxOutputTokens,
        Func<string, T> parse,
        CancellationToken ct,
        BinaryData? jsonSchema = null)
    {
        ArgumentNullException.ThrowIfNull(parse);

        if (!await TryConsumeAsync(ct))
        {
            return new AiOutcome<T>(default, AiResults.QuotaSpent(DailyLimit), 0);
        }

        var remaining = await RemainingAsync(ct);
        var prompt = prompts.Get(promptName);
        AiResult result;
        try
        {
            result = await ai.CompleteAsync(
                new AiRequest(prompt.Text, userPrompt, maxOutputTokens, jsonSchema is null ? null : promptName.Replace('-', '_'), jsonSchema),
                ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogCallFailed(e, prompt.Name, prompt.Version);
            return new AiOutcome<T>(default, AiResults.Unusable(), remaining);
        }

        await RecordTokensAsync(result, ct);

        try
        {
            return new AiOutcome<T>(parse(result.Text), null, remaining);
        }
        catch (AiAnswerException e)
        {
            LogAnswerRejected(prompt.Name, prompt.Version, e.Message);
            return new AiOutcome<T>(default, AiResults.Unusable(), remaining);
        }
    }

    /// <summary>Adds one request to today's row unless the cap is reached. Safe under concurrent calls.</summary>
    private async Task<bool> TryConsumeAsync(CancellationToken ct)
    {
        var userId = user.Id;
        var today = Today();
        var limit = DailyLimit;
        if (limit == 0)
        {
            return false;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var updated = await db.AiUsage
                .Where(u => u.UserId == userId && u.Date == today && u.Requests < limit)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Requests, u => u.Requests + 1), ct);
            if (updated == 1)
            {
                return true;
            }

            if (await db.AiUsage.AnyAsync(u => u.UserId == userId && u.Date == today, ct))
            {
                return false;
            }

            var usage = new AiUsage { UserId = userId, Date = today, Requests = 1 };
            db.AiUsage.Add(usage);
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException)
            {
                // Another request created today's row first; the next attempt counts against it.
            }
            finally
            {
                // Token totals are updated in the database, so the slice's tracker never needs this row.
                db.Entry(usage).State = EntityState.Detached;
            }
        }

        return false;
    }

    private Task<int> RecordTokensAsync(AiResult result, CancellationToken ct)
    {
        var userId = user.Id;
        var today = Today();
        return db.AiUsage
            .Where(u => u.UserId == userId && u.Date == today)
            .ExecuteUpdateAsync(
                s => s.SetProperty(u => u.InputTokens, u => u.InputTokens + result.InputTokens)
                      .SetProperty(u => u.OutputTokens, u => u.OutputTokens + result.OutputTokens),
                ct);
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI call {Prompt} v{Version} failed.")]
    private partial void LogCallFailed(Exception exception, string prompt, int version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI answer for {Prompt} v{Version} rejected: {Reason}")]
    private partial void LogAnswerRejected(string prompt, int version, string reason);
}
