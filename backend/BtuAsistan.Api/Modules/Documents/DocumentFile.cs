namespace BtuAsistan.Api.Modules.Documents;

// The uploaded file itself, in its own table so that listing documents never loads file
// contents.
public class DocumentFile
{
    public Guid DocumentId { get; set; }

    public required byte[] Content { get; set; }
}
