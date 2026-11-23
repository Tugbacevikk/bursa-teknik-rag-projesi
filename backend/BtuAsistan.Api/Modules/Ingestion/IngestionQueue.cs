using System.Threading.Channels;

namespace BtuAsistan.Api.Modules.Ingestion;

// Where the upload endpoints hand a stored document to the background worker (PLANNING P10).
public interface IIngestionQueue
{
    ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default);
}

// In-memory queue between the endpoints (writers) and the background worker (reader).
// It only carries which document to look at; the document's real state lives in the
// database. Messages are lost when the app stops; startup recovery re-enqueues unfinished
// documents.
public sealed class ChannelIngestionQueue : IIngestionQueue
{
    // At most this many waiting messages; when full, writers wait for room instead of
    // growing memory without limit.
    public const int Capacity = 100;

    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,

            // One worker: documents are processed one at a time.
            SingleReader = true,
        });

    public ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(documentId, cancellationToken);

    // Yields messages in arrival order, waiting while the queue is empty, until
    // cancellationToken fires (app shutdown).
    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
