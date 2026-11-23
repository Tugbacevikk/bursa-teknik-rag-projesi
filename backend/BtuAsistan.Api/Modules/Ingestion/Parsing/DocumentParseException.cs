namespace BtuAsistan.Api.Modules.Ingestion.Parsing;

// Thrown by readers for files that cannot be processed. The message is shown to the
// uploader and stored in documents.failure_reason, so it is written for them, in Turkish:
// what is wrong with the file, never stack traces or paths.
public sealed class DocumentParseException : Exception
{
    public DocumentParseException(string message)
        : base(message)
    {
    }

    public DocumentParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
