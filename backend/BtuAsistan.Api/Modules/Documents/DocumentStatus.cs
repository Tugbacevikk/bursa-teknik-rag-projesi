namespace BtuAsistan.Api.Modules.Documents;

public enum DocumentStatus
{
    // Stored and waiting in the queue.
    Pending,

    // Being read, chunked and embedded.
    Processing,

    // Searchable.
    Done,

    // Will not be retried; FailureReason says why.
    Failed,
}
