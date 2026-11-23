using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BtuAsistan.Api.Tests.Infrastructure;

// One server-sent event of POST /api/chat: its type ("conversation", "delta"…) and data.
public sealed record ChatEvent(string Type, JsonElement Data)
{
    public string Text(string property) => Data.GetProperty(property).GetString()!;
}

/// <summary>
/// Asks the chat as the browser does and reads the whole answer as events. The stream ends
/// when the answer does, so reading it to the end is enough.
/// </summary>
public static class ChatStream
{
    public static async Task<(HttpStatusCode Status, List<ChatEvent> Events)> AskAsync(
        HttpClient client, string question, Guid? conversationId = null)
    {
        var response = await client.PostAsJsonAsync("/api/chat", new { question, conversationId }, TestContext.Current.CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (response.StatusCode, []);
        }

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, Parse(body));
    }

    public static List<ChatEvent> Parse(string body)
    {
        var events = new List<ChatEvent>();
        foreach (var block in body.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string? type = null;
            string? data = null;
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    type = line[6..].Trim();
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    data = line[5..].Trim();
                }
            }

            if (type is not null && data is not null)
            {
                events.Add(new ChatEvent(type, JsonDocument.Parse(data).RootElement.Clone()));
            }
        }

        return events;
    }

    public static Guid ConversationId(this List<ChatEvent> events) =>
        events.Single(e => e.Type == "conversation").Data.GetProperty("conversationId").GetGuid();

    public static string Answer(this List<ChatEvent> events) =>
        string.Concat(events.Where(e => e.Type == "delta").Select(e => e.Text("text")));
}
