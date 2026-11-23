using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Chat;
using BtuAsistan.Api.Modules.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BtuAsistan.Api.Modules.Analytics;

// What students ask and how it goes (PLANNING D5), for every staff account: editors are the
// ones who add the missing documents. Every list takes ?from=&to= (dates, both included,
// in the school's time zone; default the last 30 days) and counts messages by when they
// were written.
public static partial class AnalyticsEndpoints
{
    private const int DefaultDays = 30;
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics").RequireAuthorization(Policies.Staff);

        group.MapGet("/summary", SummaryAsync);
        group.MapGet("/unanswered", UnansweredAsync);
        group.MapGet("/negative-feedback", NegativeFeedbackAsync);
        group.MapGet("/top-documents", TopDocumentsAsync);
        group.MapGet("/top-questions", TopQuestionsAsync);
        group.MapGet("/conversations/{conversationId:guid}", ConversationAsync);

        return app;
    }

    private static async Task<Results<Ok<AnalyticsSummaryResponse>, ValidationProblem>> SummaryAsync(
        DateOnly? from, DateOnly? to,
        AppDbContext db, SchoolTimeZone timeZone, TimeProvider timeProvider, IOptions<AnalyticsOptions> options,
        CancellationToken cancellationToken)
    {
        if (!TryGetRange(from, to, null, timeZone, timeProvider, options.Value, out var range, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Grouped in Postgres by the day in the school's time zone: Npgsql turns
        // ConvertTimeBySystemTimeZoneId into AT TIME ZONE.
        var rows = await db.Messages
            .Where(m => m.CreatedAt >= range.Start && m.CreatedAt < range.End)
            .GroupBy(m => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(m.CreatedAt.UtcDateTime, timeZone.Id).Date)
            .Select(g => new
            {
                Day = g.Key,
                Questions = g.Count(m => m.Role == MessageRole.User),
                Answered = g.Count(m => m.Outcome == MessageOutcome.Answered),
                NotFound = g.Count(m => m.Outcome == MessageOutcome.NotFound),
                Up = g.Count(m => m.Feedback == MessageFeedback.Up),
                Down = g.Count(m => m.Feedback == MessageFeedback.Down),
            })
            .ToDictionaryAsync(r => DateOnly.FromDateTime(r.Day), cancellationToken);

        var days = new List<AnalyticsDay>();
        for (var day = range.Period.From; day <= range.Period.To; day = day.AddDays(1))
        {
            days.Add(rows.TryGetValue(day, out var r)
                ? new AnalyticsDay(day, r.Questions, r.Answered, r.NotFound, r.Up, r.Down)
                : new AnalyticsDay(day, 0, 0, 0, 0, 0));
        }

        return TypedResults.Ok(new AnalyticsSummaryResponse(
            range.Period,
            days.Sum(d => d.Questions),
            days.Sum(d => d.Answered),
            days.Sum(d => d.NotFound),
            days.Sum(d => d.Up),
            days.Sum(d => d.Down),
            days));
    }

    // The questions the documents did not answer, newest first: what to add next.
    private static Task<Results<Ok<AnalyticsListResponse<AnsweredQuestion>>, ValidationProblem>> UnansweredAsync(
        DateOnly? from, DateOnly? to, int? limit,
        AppDbContext db, SchoolTimeZone timeZone, TimeProvider timeProvider, IOptions<AnalyticsOptions> options,
        CancellationToken cancellationToken) =>
        AnswersAsync(m => m.Outcome == MessageOutcome.NotFound, from, to, limit, db, timeZone, timeProvider, options.Value, cancellationToken);

    // Answers given a 👎, with the student's comment.
    private static Task<Results<Ok<AnalyticsListResponse<AnsweredQuestion>>, ValidationProblem>> NegativeFeedbackAsync(
        DateOnly? from, DateOnly? to, int? limit,
        AppDbContext db, SchoolTimeZone timeZone, TimeProvider timeProvider, IOptions<AnalyticsOptions> options,
        CancellationToken cancellationToken) =>
        AnswersAsync(m => m.Feedback == MessageFeedback.Down, from, to, limit, db, timeZone, timeProvider, options.Value, cancellationToken);

    private static async Task<Results<Ok<AnalyticsListResponse<AnsweredQuestion>>, ValidationProblem>> AnswersAsync(
        System.Linq.Expressions.Expression<Func<Message, bool>> filter,
        DateOnly? from, DateOnly? to, int? limit,
        AppDbContext db, SchoolTimeZone timeZone, TimeProvider timeProvider, AnalyticsOptions options,
        CancellationToken cancellationToken)
    {
        if (!TryGetRange(from, to, limit, timeZone, timeProvider, options, out var range, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var answers = db.Messages
            .Where(m => m.Role == MessageRole.Assistant && m.CreatedAt >= range.Start && m.CreatedAt < range.End)
            .Where(filter);

        var total = await answers.CountAsync(cancellationToken);

        // The question is the chat's last question before the answer (both are stored
        // together, the question first); a subquery per row, translated by EF.
        var rows = await answers
            .OrderByDescending(a => a.CreatedAt)
            .Take(range.Limit)
            .Select(a => new
            {
                a.ConversationId,
                a.Id,
                Question = db.Messages
                    .Where(q => q.ConversationId == a.ConversationId && q.Role == MessageRole.User && q.CreatedAt <= a.CreatedAt)
                    .OrderByDescending(q => q.CreatedAt)
                    .Select(q => q.Content)
                    .FirstOrDefault(),
                a.Content,
                a.Outcome,
                a.Feedback,
                a.FeedbackComment,
                a.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new AnalyticsListResponse<AnsweredQuestion>(
            range.Period,
            total,
            rows.Select(r => new AnsweredQuestion(
                r.ConversationId, r.Id, r.Question ?? "", r.Content, r.Outcome!.Value.ToString(),
                r.Feedback?.ToString(), r.FeedbackComment, r.CreatedAt)).ToList()));
    }

    // The documents answers point at: only the sources an answer cites with [n], not every
    // source the model was given.
    private static async Task<Results<Ok<AnalyticsListResponse<TopDocument>>, ValidationProblem>> TopDocumentsAsync(
        DateOnly? from, DateOnly? to, int? limit,
        AppDbContext db, SchoolTimeZone timeZone, TimeProvider timeProvider, IOptions<AnalyticsOptions> options,
        CancellationToken cancellationToken)
    {
        if (!TryGetRange(from, to, limit, timeZone, timeProvider, options.Value, out var range, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Counted here rather than in SQL: which sources are cited is in the answer's text.
        var answers = await db.Messages.AsNoTracking()
            .Where(m => m.Outcome == MessageOutcome.Answered && m.CreatedAt >= range.Start && m.CreatedAt < range.End)
            .Select(m => new { m.Content, m.Sources })
            .ToListAsync(cancellationToken);

        var cited = answers
            .SelectMany(a =>
            {
                var numbers = CitedNumbers(a.Content);
                return a.Sources.Where(s => numbers.Contains(s.Number)).DistinctBy(s => s.DocumentId);
            })
            .GroupBy(s => s.DocumentId)
            .Select(g => (DocumentId: g.Key, Answers: g.Count(), Stored: g.Last()))
            .OrderByDescending(d => d.Answers)
            .ToList();

        var ids = cited.Take(range.Limit).Select(d => d.DocumentId).ToList();
        var current = await db.Documents.AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => new { d.Title, d.Unit }, cancellationToken);

        var items = cited.Take(range.Limit).Select(d => current.TryGetValue(d.DocumentId, out var doc)
            ? new TopDocument(d.DocumentId, doc.Title, doc.Unit, Deleted: false, d.Answers)
            : new TopDocument(d.DocumentId, d.Stored.Title, d.Stored.Unit, Deleted: true, d.Answers)).ToList();

        return TypedResults.Ok(new AnalyticsListResponse<TopDocument>(range.Period, cited.Count, items));
    }

    // Questions asked most often, grouped by their text with case, punctuation and extra
    // spaces ignored. "İMEP başvurusu ne zaman?" and "imep başvurusu ne zaman" are one;
    // the same question in other words is not (grouping by meaning is out of scope, D5).
    private static async Task<Results<Ok<AnalyticsListResponse<TopQuestion>>, ValidationProblem>> TopQuestionsAsync(
        DateOnly? from, DateOnly? to, int? limit,
        AppDbContext db, SchoolTimeZone timeZone, TimeProvider timeProvider, IOptions<AnalyticsOptions> options,
        CancellationToken cancellationToken)
    {
        if (!TryGetRange(from, to, limit, timeZone, timeProvider, options.Value, out var range, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Grouped here: Turkish lower-casing (İ → i, I → ı) depends on the database's
        // collation in SQL.
        var questions = await db.Messages.AsNoTracking()
            .Where(m => m.Role == MessageRole.User && m.CreatedAt >= range.Start && m.CreatedAt < range.End)
            .Select(m => new { m.Content, m.CreatedAt })
            .ToListAsync(cancellationToken);

        var groups = questions
            .GroupBy(q => NormalizeQuestion(q.Content))
            .Where(g => g.Key.Length > 0)
            .Select(g =>
            {
                var latest = g.MaxBy(q => q.CreatedAt)!;
                return new TopQuestion(latest.Content, g.Count(), latest.CreatedAt);
            })
            .OrderByDescending(q => q.Count)
            .ThenByDescending(q => q.LastAskedAt)
            .ToList();

        return TypedResults.Ok(new AnalyticsListResponse<TopQuestion>(range.Period, groups.Count, groups.Take(range.Limit).ToList()));
    }

    // One chat with everything stored about it, opened from a row of the lists above.
    private static async Task<Results<Ok<StaffConversationResponse>, NotFound>> ConversationAsync(
        Guid conversationId, AppDbContext db, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .Include(c => c.Messages)
            .SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new StaffConversationResponse(
            conversation.Id,
            conversation.CreatedAt,
            conversation.Messages
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .Select(m => new StaffMessage(
                    m.Id,
                    m.Role.ToString(),
                    m.Content,
                    m.Outcome?.ToString(),
                    m.Sources.Select(s => new StaffSource(s.Number, s.DocumentId, s.Title, s.Unit, s.SectionPath, s.PageNumber)).ToList(),
                    m.Model,
                    m.DurationMs,
                    m.Feedback?.ToString(),
                    m.FeedbackComment,
                    m.CreatedAt))
                .ToList()));
    }

    private sealed record Range(AnalyticsPeriod Period, DateTimeOffset Start, DateTimeOffset End, int Limit);

    private static bool TryGetRange(
        DateOnly? from, DateOnly? to, int? limit,
        SchoolTimeZone timeZone, TimeProvider timeProvider, AnalyticsOptions options,
        out Range range, out Dictionary<string, string[]> errors)
    {
        errors = [];
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone.Zone).DateTime);
        var last = to ?? today;
        var first = from ?? last.AddDays(-(DefaultDays - 1));

        if (first > last)
        {
            errors["from"] = ["Başlangıç tarihi bitiş tarihinden sonra olamaz."];
        }
        else if (last.DayNumber - first.DayNumber + 1 > options.MaxDays)
        {
            errors["from"] = [$"En fazla {options.MaxDays} günlük bir dönem seçilebilir."];
        }

        if (limit is < 1 or > MaxLimit)
        {
            errors["limit"] = [$"limit 1 ile {MaxLimit} arasında olmalıdır."];
        }

        range = new Range(
            new AnalyticsPeriod(first, last),
            StartOfDay(first, timeZone.Zone),
            StartOfDay(last.AddDays(1), timeZone.Zone),
            limit ?? DefaultLimit);
        return errors.Count == 0;
    }

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), zone), TimeSpan.Zero);

    private static HashSet<int> CitedNumbers(string answer) =>
        Citation().Matches(answer).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToHashSet();

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static string NormalizeQuestion(string question)
    {
        var text = new StringBuilder(question.Length);
        foreach (var c in question.ToLower(Turkish))
        {
            text.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return Whitespace().Replace(text.ToString(), " ").Trim();
    }

    // Up to four digits, so a long number in brackets cannot overflow int.Parse.
    [GeneratedRegex(@"\[(\d{1,4})\]")]
    private static partial Regex Citation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
