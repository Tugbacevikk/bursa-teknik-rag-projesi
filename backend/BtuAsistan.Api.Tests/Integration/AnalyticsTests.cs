using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BtuAsistan.Api.Modules.Analytics;
using BtuAsistan.Api.Modules.Identity;
using BtuAsistan.Api.Tests.Infrastructure;
using Npgsql;

namespace BtuAsistan.Api.Tests.Integration;

// D5: what students ask and how it goes, counted in the school's time zone.
public sealed class AnalyticsTests(AnalyticsTests.Fixture api) : IClassFixture<AnalyticsTests.Fixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<T> GetAsync<T>(string path, UserRole role = UserRole.Editor)
    {
        var client = await api.SignInAsNewAsync(role);
        return (await client.GetFromJsonAsync<T>(path, Cancel))!;
    }

    private static string Period(DateOnly from, DateOnly to) => $"from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";

    // A question at 00:30 in Istanbul is 21:30 UTC the day before: it belongs to the later
    // day, as the school sees it.
    [Fact]
    public async Task Days_are_counted_in_the_school_time_zone()
    {
        var summary = await GetAsync<AnalyticsSummaryResponse>($"/api/analytics/summary?{Period(Fixture.Day.AddDays(-1), Fixture.Day)}");

        Assert.Equal(2, summary.Days.Count);
        Assert.Equal(new AnalyticsDay(Fixture.Day.AddDays(-1), 1, 1, 0, 0, 0), summary.Days[0]);
        Assert.Equal(new AnalyticsDay(Fixture.Day, 1, 1, 0, 1, 0), summary.Days[1]);
    }

    [Fact]
    public async Task Default_period_is_the_last_30_days_with_every_day_listed()
    {
        var summary = await GetAsync<AnalyticsSummaryResponse>("/api/analytics/summary");

        Assert.Equal(30, summary.Days.Count);
        Assert.Equal(Fixture.Today, summary.Period.To);
        Assert.Equal((3, 2, 1, 1, 1), (summary.Questions, summary.Answered, summary.NotFound, summary.Up, summary.Down));
    }

    [Fact]
    public async Task Unanswered_questions_come_with_the_comment()
    {
        var list = await GetAsync<AnalyticsListResponse<AnsweredQuestion>>("/api/analytics/unanswered");

        var item = Assert.Single(list.Items);
        Assert.Equal("Yemekhane saat kaçta açılıyor?", item.Question);
        Assert.Equal("yemekhane bilgisi lazım", item.FeedbackComment);
        Assert.Equal("NotFound", item.Outcome);
    }

    [Fact]
    public async Task Negative_feedback_lists_the_thumbs_down_answers()
    {
        var list = await GetAsync<AnalyticsListResponse<AnsweredQuestion>>("/api/analytics/negative-feedback");

        Assert.Equal(Fixture.NotFoundChat, Assert.Single(list.Items).ConversationId);
    }

    // Only sources the answer cites with [n] count, not all eight the model was given.
    [Fact]
    public async Task Documents_are_counted_by_citation()
    {
        var list = await GetAsync<AnalyticsListResponse<TopDocument>>("/api/analytics/top-documents");

        Assert.Equal(2, list.Total);
        Assert.Equal((Fixture.DocA, 2, "SSS"), (list.Items[0].DocumentId, list.Items[0].Answers, list.Items[0].Title));
        Assert.Equal((Fixture.DocB, 1), (list.Items[1].DocumentId, list.Items[1].Answers));
        Assert.All(list.Items, item => Assert.True(item.Deleted));
    }

    // Case, punctuation and extra spaces are ignored; Turkish İ/ı are lower-cased right.
    [Fact]
    public async Task Same_question_in_other_case_and_punctuation_is_one_row()
    {
        var list = await GetAsync<AnalyticsListResponse<TopQuestion>>("/api/analytics/top-questions");

        Assert.Equal(2, list.Total);
        Assert.Equal(("İMEP başvurusu ne zaman?", 2), (list.Items[0].Question, list.Items[0].Count));
    }

    [Fact]
    public async Task Limit_cuts_the_list_but_not_the_total()
    {
        var list = await GetAsync<AnalyticsListResponse<TopQuestion>>("/api/analytics/top-questions?limit=1");

        Assert.Single(list.Items);
        Assert.Equal(2, list.Total);
    }

    [Fact]
    public async Task Staff_see_a_whole_chat_with_its_internals()
    {
        var chat = await GetAsync<JsonElement>($"/api/analytics/conversations/{Fixture.AnsweredChat}");

        var answer = chat.GetProperty("messages")[1];
        Assert.Equal("fake-model", answer.GetProperty("model").GetString());
        Assert.Equal(Fixture.DocA, answer.GetProperty("sources")[0].GetProperty("documentId").GetGuid());
    }

    [Theory]
    [InlineData("summary?from=2026-11-10&to=2026-11-01")]
    [InlineData("summary?from=2020-01-01&to=2026-11-01")]
    [InlineData("unanswered?limit=0")]
    [InlineData("unanswered?limit=201")]
    public async Task Invalid_period_or_limit_is_refused(string query)
    {
        var client = await api.SignInAsNewAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/analytics/{query}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Students_cannot_see_analytics()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonymous().GetAsync("/api/analytics/summary", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.SignInAsNewAsync()).GetAsync($"/api/analytics/conversations/{Guid.NewGuid()}", Cancel)).StatusCode);
    }

    public sealed class Fixture : ApiFixture
    {
        private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

        public static readonly DateOnly Today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Istanbul).DateTime);

        // Ten days ago, well inside the default 30 days and the 90-day retention.
        public static readonly DateOnly Day = Today.AddDays(-10);

        public static readonly Guid AnsweredChat = Guid.NewGuid();
        public static readonly Guid NotFoundChat = Guid.NewGuid();
        public static readonly Guid DocA = Guid.NewGuid();
        public static readonly Guid DocB = Guid.NewGuid();
        private static readonly Guid DocC = Guid.NewGuid();

        protected override async Task SeedBeforeStartAsync(NpgsqlConnection owner)
        {
            // Istanbul is UTC+3 all year.
            var midnightUtc = new DateTimeOffset(Day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(-3);

            // 00:30 in Istanbul on Day; cites [1] and [2] of three sources; 👍.
            await ChatAsync(owner, AnsweredChat, midnightUtc.AddMinutes(30), "İMEP başvurusu ne zaman?", "Answered",
                "Temmuz'da [1], kesin tarih [2].", Sources((1, DocA, "SSS"), (2, DocB, "Takvim"), (3, DocC, "Yönerge")), "Up", null);

            // 23:00 in Istanbul the day before; the same question written differently.
            await ChatAsync(owner, Guid.NewGuid(), midnightUtc.AddHours(-1), "imep   BAŞVURUSU ne zaman", "Answered",
                "Temmuz [1].", Sources((1, DocA, "SSS")), null, null);

            // Five days before; not found, 👎 with a comment.
            await ChatAsync(owner, NotFoundChat, midnightUtc.AddDays(-5).AddHours(12), "Yemekhane saat kaçta açılıyor?", "NotFound",
                "Belgelerde bulamadım.", "[]", "Down", "yemekhane bilgisi lazım");
        }

        private static string Sources(params (int Number, Guid Document, string Title)[] sources) =>
            JsonSerializer.Serialize(sources.Select(s => new
            {
                s.Number,
                DocumentId = s.Document,
                ChunkId = Guid.NewGuid(),
                s.Title,
                Unit = "İMEP",
            }));

        private static async Task ChatAsync(
            NpgsqlConnection owner, Guid id, DateTimeOffset askedAt, string question, string outcome, string answer, string sources,
            string? feedback, string? comment)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO conversations (id, created_at, last_message_at) VALUES (@id, @asked, @answered);
                INSERT INTO messages (id, conversation_id, role, content, outcome, sources, created_at)
                    VALUES (gen_random_uuid(), @id, 'User', @question, NULL, '[]', @asked);
                INSERT INTO messages (id, conversation_id, role, content, outcome, sources, model, feedback, feedback_comment, feedback_at, created_at)
                    VALUES (gen_random_uuid(), @id, 'Assistant', @answer, @outcome, @sources::jsonb, 'fake-model', @feedback, @comment, @feedbackAt, @answered);
                """, owner);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("asked", askedAt);
            command.Parameters.AddWithValue("answered", askedAt.AddSeconds(2));
            command.Parameters.AddWithValue("question", question);
            command.Parameters.AddWithValue("answer", answer);
            command.Parameters.AddWithValue("outcome", outcome);
            command.Parameters.AddWithValue("sources", sources);
            command.Parameters.Add(new NpgsqlParameter("feedback", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)feedback ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("comment", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)comment ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("feedbackAt", NpgsqlTypes.NpgsqlDbType.TimestampTz)
            {
                Value = feedback is null ? DBNull.Value : askedAt.AddSeconds(2),
            });
            await command.ExecuteNonQueryAsync();
        }
    }
}
