using System.Net;
using System.Net.Http.Json;
using BtuAsistan.Api.Modules.Chat;
using BtuAsistan.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Tests.Integration;

// C11: questions per address; RateLimiting:ChatPerMinute lowered to 2 here.
public sealed class ChatRateLimitTests(ChatRateLimitTests.Fixture api) : IClassFixture<ChatRateLimitTests.Fixture>
{
    [Fact]
    public async Task Third_question_in_a_minute_gets_429_with_retry_after()
    {
        var client = api.Anonymous();
        Assert.Equal(HttpStatusCode.OK, (await ChatStream.AskAsync(client, "Sigorta?")).Status);
        Assert.Equal(HttpStatusCode.OK, (await ChatStream.AskAsync(client, "Başvuru?")).Status);

        var refused = await client.PostAsJsonAsync("/api/chat", new { question = "Takvim?" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.InRange(int.Parse(Assert.Single(refused.Headers.GetValues("Retry-After"))), 1, 60);
    }

    public sealed class Fixture : SeededApiFixture
    {
        protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
        {
            ["RateLimiting:ChatPerMinute"] = "2",
        };
    }
}

// C11: answers written at once, for everyone; here one, with nobody waiting in line.
public sealed class ChatBusyTests(ChatBusyTests.Fixture api) : IClassFixture<ChatBusyTests.Fixture>
{
    [Fact]
    public async Task Question_while_the_answers_are_full_gets_503_busy()
    {
        var cancel = TestContext.Current.CancellationToken;
        var release = new TaskCompletionSource();
        api.Answers.Respond = _ => FakeAnswerGenerator.WaitFor(release.Task);

        // The first answer starts and waits; its headers are out, so it holds the only place.
        using var first = await api.Anonymous().SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = JsonContent.Create(new { question = "Sigorta?" }) },
            HttpCompletionOption.ResponseHeadersRead,
            cancel);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await api.Anonymous().PostAsJsonAsync("/api/chat", new { question = "Başvuru?" }, cancel);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Contains(new ChatOptions().BusyMessage, await second.Content.ReadAsStringAsync(cancel));

        // Once the first answer is done the place is free again.
        release.SetResult();
        var events = ChatStream.Parse(await first.Content.ReadAsStringAsync(cancel));
        Assert.Equal("done", events.Last().Type);
        api.Answers.Respond = _ => FakeAnswerGenerator.Answer("Cevap [1].");
        Assert.Equal(HttpStatusCode.OK, (await ChatStream.AskAsync(api.Anonymous(), "Takvim?")).Status);
    }

    public sealed class Fixture : SeededApiFixture
    {
        protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
        {
            ["Chat:MaxConcurrentAnswers"] = "1",
            ["Chat:MaxQueuedAnswers"] = "0",
        };
    }
}

// C12: an answer that does not finish in Chat:GenerationTimeoutSeconds (here the minimum,
// 5 s) is given up; the student gets the "try again" message and nothing is stored.
public sealed class ChatTimeoutTests(ChatTimeoutTests.Fixture api) : IClassFixture<ChatTimeoutTests.Fixture>
{
    [Fact]
    public async Task Answer_that_takes_too_long_is_an_error_and_nothing_is_stored()
    {
        api.Answers.Respond = _ => FakeAnswerGenerator.WaitFor(Task.Delay(Timeout.Infinite));

        var (_, events) = await ChatStream.AskAsync(api.Anonymous(), "Sigorta?");

        Assert.Equal(["conversation", "sources", "error"], events.Select(e => e.Type));
        Assert.Equal(new ChatOptions().UnavailableMessage, events.Last().Text("message"));
        var conversationId = events.ConversationId();
        Assert.False(await api.WithDbAsync(db => db.Conversations.AnyAsync(c => c.Id == conversationId, TestContext.Current.CancellationToken)));
    }

    public sealed class Fixture : SeededApiFixture
    {
        protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
        {
            ["Chat:GenerationTimeoutSeconds"] = "5",
        };
    }
}
