using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BtuAsistan.Api.Modules.Chat;
using BtuAsistan.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Tests.Integration;

// D2–D3: 👍/👎 on an answer, by whoever knows the chat's id.
public sealed class FeedbackTests(SeededApiFixture api) : IClassFixture<SeededApiFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // A chat with one answer: its ids.
    private async Task<(Guid Conversation, Guid Question, Guid Answer)> AnsweredChatAsync()
    {
        var (_, events) = await ChatStream.AskAsync(api.Anonymous(), "Sigortayı kim öder?");
        var conversationId = events.ConversationId();
        var ids = await api.WithDbAsync(db => db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Id)
            .ToListAsync(Cancel));
        return (conversationId, ids[0], ids[1]);
    }

    private Task<HttpResponseMessage> RateAsync(Guid conversation, Guid message, object body) =>
        api.Anonymous().PutAsJsonAsync($"/api/chat/{conversation}/messages/{message}/feedback", body, Cancel);

    private Task<Message> MessageAsync(Guid id) =>
        api.WithDbAsync(db => db.Messages.SingleAsync(m => m.Id == id, Cancel));

    [Fact]
    public async Task Thumbs_down_with_a_comment_is_stored_masked()
    {
        var chat = await AnsweredChatAsync();

        var response = await RateAsync(chat.Conversation, chat.Answer, new { rating = "down", comment = "Yanlış; beni 0532 123 45 67'den arayın" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var message = await MessageAsync(chat.Answer);
        Assert.Equal(MessageFeedback.Down, message.Feedback);
        Assert.Equal($"Yanlış; beni {PersonalDataMasker.PhoneMask}'den arayın", message.FeedbackComment);
        Assert.NotNull(message.FeedbackAt);
    }

    // The last one given counts; a comment only explains a 👎.
    [Fact]
    public async Task Later_thumbs_up_replaces_and_clears_the_comment()
    {
        var chat = await AnsweredChatAsync();
        await RateAsync(chat.Conversation, chat.Answer, new { rating = "down", comment = "eksik" });

        await RateAsync(chat.Conversation, chat.Answer, new { rating = "up", comment = "bu tutulmaz" });

        var message = await MessageAsync(chat.Answer);
        Assert.Equal(MessageFeedback.Up, message.Feedback);
        Assert.Null(message.FeedbackComment);
    }

    [Fact]
    public async Task Feedback_comes_back_with_the_chat()
    {
        var chat = await AnsweredChatAsync();
        await RateAsync(chat.Conversation, chat.Answer, new { rating = "down", comment = "tarih yok" });

        var stored = await api.Anonymous().GetFromJsonAsync<JsonElement>($"/api/chat/{chat.Conversation}", Cancel);

        var answer = stored.GetProperty("messages")[1];
        Assert.Equal("Down", answer.GetProperty("feedback").GetString());
        Assert.Equal("tarih yok", answer.GetProperty("feedbackComment").GetString());
    }

    // D3: knowing one chat's id gives no say over another's answers.
    [Fact]
    public async Task Answer_of_another_chat_cannot_be_rated()
    {
        var mine = await AnsweredChatAsync();
        var theirs = await AnsweredChatAsync();

        var response = await RateAsync(mine.Conversation, theirs.Answer, new { rating = "down" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null((await MessageAsync(theirs.Answer)).Feedback);
    }

    [Fact]
    public async Task Question_cannot_be_rated()
    {
        var chat = await AnsweredChatAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await RateAsync(chat.Conversation, chat.Question, new { rating = "up" })).StatusCode);
    }

    // Feedback does not keep a chat from being deleted after 90 days.
    [Fact]
    public async Task Feedback_does_not_extend_the_chat()
    {
        var chat = await AnsweredChatAsync();
        var before = await api.WithDbAsync(db => db.Conversations.Where(c => c.Id == chat.Conversation).Select(c => c.LastMessageAt).SingleAsync(Cancel));

        await RateAsync(chat.Conversation, chat.Answer, new { rating = "up" });

        Assert.Equal(before, await api.WithDbAsync(db => db.Conversations.Where(c => c.Id == chat.Conversation).Select(c => c.LastMessageAt).SingleAsync(Cancel)));
    }

    [Fact]
    public async Task Not_found_answer_can_be_rated()
    {
        api.Answers.Respond = _ => FakeAnswerGenerator.NotFound();
        try
        {
            var chat = await AnsweredChatAsync();

            Assert.Equal(HttpStatusCode.NoContent, (await RateAsync(chat.Conversation, chat.Answer, new { rating = "down", comment = "bu bilgi lazım" })).StatusCode);
        }
        finally
        {
            api.Answers.Respond = _ => FakeAnswerGenerator.Answer("Cevap [1].");
        }
    }

    [Fact]
    public async Task Invalid_feedback_is_refused()
    {
        var chat = await AnsweredChatAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await RateAsync(chat.Conversation, chat.Answer, new { rating = "1" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RateAsync(chat.Conversation, chat.Answer, new { rating = "down", comment = new string('x', 501) })).StatusCode);
    }
}

// D3: RateLimiting:FeedbackPerMinute, lowered to 2 here.
public sealed class FeedbackRateLimitTests(FeedbackRateLimitTests.Fixture api) : IClassFixture<FeedbackRateLimitTests.Fixture>
{
    [Fact]
    public async Task Third_feedback_in_a_minute_gets_429()
    {
        var cancel = TestContext.Current.CancellationToken;
        var (_, events) = await ChatStream.AskAsync(api.Anonymous(), "Sigorta?");
        var conversationId = events.ConversationId();
        var answerId = events.Single(e => e.Type == "done").Data.GetProperty("messageId").GetGuid();
        var url = $"/api/chat/{conversationId}/messages/{answerId}/feedback";

        Assert.Equal(HttpStatusCode.NoContent, (await api.Anonymous().PutAsJsonAsync(url, new { rating = "up" }, cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Anonymous().PutAsJsonAsync(url, new { rating = "down" }, cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await api.Anonymous().PutAsJsonAsync(url, new { rating = "up" }, cancel)).StatusCode);
    }

    public sealed class Fixture : SeededApiFixture
    {
        protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
        {
            ["RateLimiting:FeedbackPerMinute"] = "2",
        };
    }
}
