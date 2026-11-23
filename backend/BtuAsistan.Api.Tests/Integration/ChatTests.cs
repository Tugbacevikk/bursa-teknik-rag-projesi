using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BtuAsistan.Api.Modules.Chat;
using BtuAsistan.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Tests.Integration;

// Faz 4: the students' chat (PLANNING C1–C12), with the chat model faked.
public sealed class ChatTests : IClassFixture<SeededApiFixture>
{
    private readonly SeededApiFixture api;

    public ChatTests(SeededApiFixture api)
    {
        this.api = api;

        // Tests of a class run one after another and share the fake; each starts clean.
        api.Answers.Respond = _ => FakeAnswerGenerator.Answer("Sigortayı ", "Rektörlük karşılar [1].");
        api.Answers.Requests.Clear();
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Answer_streams_as_events_in_order()
    {
        var (status, events) = await ChatStream.AskAsync(api.Anonymous(), "İş kazası sigortasını kim karşılar?");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["conversation", "sources", "delta", "delta", "done"], events.Select(e => e.Type));
        Assert.Equal("Sigortayı Rektörlük karşılar [1].", events.Answer());

        var first = events.Single(e => e.Type == "sources").Data.GetProperty("sources")[0];
        Assert.Equal(1, first.GetProperty("number").GetInt32());
        Assert.Equal(SeededApiFixture.Regulation, first.GetProperty("title").GetString());
        Assert.Equal("Sigorta", first.GetProperty("sectionPath").GetString());
    }

    // What students see of a source carries no internal ids (ChatSource).
    [Fact]
    public async Task Sources_sent_to_students_have_no_internal_ids()
    {
        var (_, events) = await ChatStream.AskAsync(api.Anonymous(), "Sigorta");

        var source = events.Single(e => e.Type == "sources").Data.GetProperty("sources")[0];
        Assert.False(source.TryGetProperty("documentId", out _));
        Assert.False(source.TryGetProperty("chunkId", out _));
    }

    [Fact]
    public async Task Model_gets_the_question_and_numbered_sources_with_their_headers()
    {
        await ChatStream.AskAsync(api.Anonymous(), "İş kazası sigortasını kim karşılar?");

        var request = Assert.Single(api.Answers.Requests);
        Assert.Equal("İş kazası sigortasını kim karşılar?", request.Question);
        Assert.Empty(request.History);
        Assert.Equal(Enumerable.Range(1, request.Sources.Count), request.Sources.Select(s => s.N));
        Assert.StartsWith("Belge: İMEP Yönergesi > Sigorta", request.Sources[0].Text);
    }

    [Fact]
    public async Task Question_and_answer_are_stored_together()
    {
        var (_, events) = await ChatStream.AskAsync(api.Anonymous(), "Sigortayı kim öder?");
        var conversationId = events.ConversationId();

        var messages = await MessagesAsync(conversationId);

        Assert.Equal([MessageRole.User, MessageRole.Assistant], messages.Select(m => m.Role));
        Assert.Equal("Sigortayı kim öder?", messages[0].Content);
        Assert.Equal(MessageOutcome.Answered, messages[1].Outcome);
        Assert.Equal(FakeAnswerGenerator.ModelName, messages[1].Model);
        Assert.NotEmpty(messages[1].Sources);
        Assert.Equal(events.Single(e => e.Type == "done").Data.GetProperty("messageId").GetGuid(), messages[1].Id);
    }

    // C10: masked before search, before the model, before storing.
    [Fact]
    public async Task Personal_data_never_reaches_the_model_or_the_database()
    {
        var (_, events) = await ChatStream.AskAsync(api.Anonymous(), "TC'm 10000000146, tel 0532 123 45 67, sigortam var mı?");

        var masked = $"TC'm {PersonalDataMasker.IdNumberMask}, tel {PersonalDataMasker.PhoneMask}, sigortam var mı?";
        Assert.Equal(masked, Assert.Single(api.Answers.Requests).Question);
        Assert.Equal(masked, (await MessagesAsync(events.ConversationId()))[0].Content);
    }

    [Fact]
    public async Task Model_saying_not_found_gives_the_fixed_message()
    {
        api.Answers.Respond = _ => FakeAnswerGenerator.NotFound();

        var (_, events) = await ChatStream.AskAsync(api.Anonymous(), "Yemekhane kaçta açılır?");

        Assert.Equal(["conversation", "sources", "not_found"], events.Select(e => e.Type));
        var notFound = events.Last();
        Assert.Equal(new ChatOptions().NotFoundMessage, notFound.Text("message"));
        var answer = (await MessagesAsync(events.ConversationId()))[1];
        Assert.Equal(MessageOutcome.NotFound, answer.Outcome);
        Assert.Equal(notFound.Data.GetProperty("messageId").GetGuid(), answer.Id);
        Assert.Empty(answer.Sources);
    }

    // C9: a broken answer is not stored; the student can ask again.
    [Fact]
    public async Task Answer_broken_off_is_an_error_and_nothing_is_stored()
    {
        api.Answers.Respond = _ => FakeAnswerGenerator.FailAfter("Sigortayı ");

        var (_, events) = await ChatStream.AskAsync(api.Anonymous(), "Sigortayı kim öder?");

        Assert.Equal(["conversation", "sources", "delta", "error"], events.Select(e => e.Type));
        Assert.Equal(new ChatOptions().UnavailableMessage, events.Last().Text("message"));
        var conversationId = events.ConversationId();
        Assert.False(await api.WithDbAsync(db => db.Conversations.AnyAsync(c => c.Id == conversationId, Cancel)));
    }

    // C4: a follow-up question sees the chat so far; old citations are taken out because
    // their numbers belong to the old sources.
    [Fact]
    public async Task Follow_up_question_gets_the_history_without_old_citations()
    {
        var client = api.Anonymous();
        var (_, first) = await ChatStream.AskAsync(client, "Sigortayı kim öder?");
        api.Answers.Requests.Clear();

        var (_, second) = await ChatStream.AskAsync(client, "Peki ya başvuru şartı?", first.ConversationId());

        Assert.Equal(first.ConversationId(), second.ConversationId());
        var history = Assert.Single(api.Answers.Requests).History;
        Assert.Equal(["user", "assistant"], history.Select(t => t.Role));
        Assert.Equal("Sigortayı kim öder?", history[0].Text);
        Assert.Equal("Sigortayı Rektörlük karşılar .", history[1].Text);
        Assert.Equal(4, (await MessagesAsync(first.ConversationId())).Count);
    }

    // An id nobody has (or one deleted after 90 days) starts a new chat with a new id.
    [Fact]
    public async Task Unknown_conversation_id_starts_a_new_chat()
    {
        var unknown = Guid.NewGuid();

        var (_, events) = await ChatStream.AskAsync(api.Anonymous(), "Sigorta", unknown);

        Assert.NotEqual(unknown, events.ConversationId());
        Assert.Empty(Assert.Single(api.Answers.Requests).History);
    }

    // C2: the browser reloads the chat as it was shown.
    [Fact]
    public async Task Chat_can_be_read_back_by_its_id()
    {
        var (_, events) = await ChatStream.AskAsync(api.Anonymous(), "Sigortayı kim öder?");

        var chat = await api.Anonymous().GetFromJsonAsync<JsonElement>($"/api/chat/{events.ConversationId()}", Cancel);

        var messages = chat.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("User", messages[0].GetProperty("role").GetString());
        Assert.Equal("Answered", messages[1].GetProperty("outcome").GetString());
        Assert.Equal("Sigortayı Rektörlük karşılar [1].", messages[1].GetProperty("content").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Anonymous().GetAsync($"/api/chat/{Guid.NewGuid()}", Cancel)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_question_is_refused(string question)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await ChatStream.AskAsync(api.Anonymous(), question)).Status);
    }

    [Fact]
    public async Task Too_long_question_is_refused()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await ChatStream.AskAsync(api.Anonymous(), new string('a', 2001))).Status);
    }

    private Task<List<Message>> MessagesAsync(Guid conversationId) =>
        api.WithDbAsync(db => db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync(Cancel));
}

// With no documents there is nothing to answer from: the model is not asked at all.
public sealed class ChatWithoutDocumentsTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Nothing_found_is_answered_without_the_model()
    {
        var (_, events) = await ChatStream.AskAsync(api.Anonymous(), "İMEP başvurusu ne zaman?");

        Assert.Equal(["conversation", "not_found"], events.Select(e => e.Type));
        Assert.Empty(api.Answers.Requests);
        var outcome = await api.WithDbAsync(db => db.Messages
            .Where(m => m.ConversationId == events.ConversationId() && m.Role == MessageRole.Assistant)
            .Select(m => new { m.Outcome, m.Model })
            .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(MessageOutcome.NotFound, outcome.Outcome);
        Assert.Null(outcome.Model);
    }
}
