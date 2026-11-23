using BtuAsistan.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BtuAsistan.Api.Tests.Integration;

// D7 / C9: chats idle for longer than Chat:RetentionDays (90) are deleted with their
// messages when the API starts (and every Chat:RetentionIntervalHours after).
public sealed class RetentionTests(RetentionTests.Fixture api) : IClassFixture<RetentionTests.Fixture>
{
    [Fact]
    public async Task Chats_idle_for_more_than_90_days_are_deleted_with_their_messages()
    {
        var cancel = TestContext.Current.CancellationToken;

        // The clean-up runs in the background as the API starts.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        List<Guid> left;
        do
        {
            left = await api.WithDbAsync(db => db.Conversations.Select(c => c.Id).ToListAsync(cancel));
            if (!left.Contains(Fixture.DaysOld100) && !left.Contains(Fixture.DaysOld91))
            {
                break;
            }

            await Task.Delay(100, cancel);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Equal(new[] { Fixture.DaysOld10, Fixture.DaysOld89 }.Order(), left.Order());
        var messagesLeft = await api.WithDbAsync(db => db.Messages.Select(m => m.ConversationId).Distinct().ToListAsync(cancel));
        Assert.Equal(left.Order(), messagesLeft.Order());
    }

    public sealed class Fixture : ApiFixture
    {
        public static readonly Guid DaysOld100 = Guid.NewGuid();
        public static readonly Guid DaysOld91 = Guid.NewGuid();
        public static readonly Guid DaysOld89 = Guid.NewGuid();
        public static readonly Guid DaysOld10 = Guid.NewGuid();

        // Batches of one, so the loop over batches is exercised too.
        protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
        {
            ["Chat:RetentionBatchSize"] = "1",
        };

        protected override async Task SeedBeforeStartAsync(NpgsqlConnection owner)
        {
            foreach (var (id, days) in new[] { (DaysOld100, 100), (DaysOld91, 91), (DaysOld89, 89), (DaysOld10, 10) })
            {
                await using var command = new NpgsqlCommand("""
                    INSERT INTO conversations (id, created_at, last_message_at)
                        VALUES (@id, now() - make_interval(days => @days + 1), now() - make_interval(days => @days));
                    INSERT INTO messages (id, conversation_id, role, content, outcome, sources, created_at) VALUES
                        (gen_random_uuid(), @id, 'User', 'soru', NULL, '[]', now() - make_interval(days => @days)),
                        (gen_random_uuid(), @id, 'Assistant', 'cevap', 'Answered', '[]', now() - make_interval(days => @days));
                    """, owner);
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("days", days);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
