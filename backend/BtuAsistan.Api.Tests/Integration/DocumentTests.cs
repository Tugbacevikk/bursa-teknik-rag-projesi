using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BtuAsistan.Api.Modules.Audit;
using BtuAsistan.Api.Modules.Documents;
using BtuAsistan.Api.Modules.Identity;
using BtuAsistan.Api.Modules.Ingestion;
using BtuAsistan.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Tests.Integration;

// Faz 2: upload, processing in the background, replacing and deleting (PLANNING P1–P7).
public sealed class DocumentTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Uploaded_document_is_processed_into_embedded_chunks()
    {
        var staff = await api.CreateStaffAsync(UserRole.Editor);
        var client = await api.SignInAsync(staff);

        var response = await client.PostAsync("/api/documents", Upload(Markdown("Staj"), "yonerge.md", "İMEP Yönergesi"), Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<DocumentCreatedResponse>(Cancel);
        Assert.Equal("Pending", created!.Status);

        var document = await DocumentHelpers.WaitForStatusAsync(client, created.Id, "Done");

        Assert.True(document.ChunkCount >= 2);
        Assert.Equal(1, document.Version);
        Assert.Equal(staff.Email, document.UploadedBy);
        var chunks = await api.WithDbAsync(db => db.Chunks.Where(c => c.DocumentId == created.Id).ToListAsync(Cancel));
        Assert.All(chunks, chunk =>
        {
            Assert.Equal("BAAI/bge-m3", chunk.EmbeddingModel);
            Assert.NotNull(chunk.Embedding);
            Assert.StartsWith("Belge: İMEP Yönergesi", chunk.Content);
        });
        Assert.True(await AuditedAsync(AuditActions.DocumentUploaded, created.Id, staff.Id));
    }

    [Fact]
    public async Task Same_file_twice_is_refused_with_the_existing_document()
    {
        var client = await api.SignInAsNewAsync();
        var content = Markdown("Takvim");
        var first = await (await client.PostAsync("/api/documents", Upload(content, "takvim.md", "Takvim"), Cancel))
            .Content.ReadFromJsonAsync<DocumentCreatedResponse>(Cancel);

        var second = await client.PostAsync("/api/documents", Upload(content, "baska-ad.md", "Başka başlık"), Cancel);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var duplicate = await second.Content.ReadFromJsonAsync<DuplicateDocumentResponse>(Cancel);
        Assert.Equal(first!.Id, duplicate!.ExistingDocumentId);
        Assert.Equal("Takvim", duplicate.ExistingTitle);
    }

    [Fact]
    public async Task Invalid_fields_and_files_are_refused()
    {
        var client = await api.SignInAsNewAsync();

        var badFields = await client.PostAsync("/api/documents", Upload(Markdown("x"), "a.md", title: " ", kind: "3", sourceUrl: "ftp://x"), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, badFields.StatusCode);
        var errors = await badFields.Content.ReadFromJsonAsync<ValidationErrors>(Cancel);
        Assert.Contains("title", errors!.Errors.Keys);
        Assert.Contains("kind", errors.Errors.Keys);
        Assert.Contains("sourceUrl", errors.Errors.Keys);

        var picture = await client.PostAsync("/api/documents", Upload([0x89, 0x50, 0x4E, 0x47, 1, 2, 3], "resim.png", "Resim"), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, picture.StatusCode);
    }

    [Fact]
    public async Task Anonymous_upload_is_refused()
    {
        var response = await api.Anonymous().PostAsync("/api/documents", Upload(Markdown("x"), "a.md", "A"), Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Replacing_the_file_makes_a_new_version_with_new_chunks()
    {
        var staff = await api.CreateStaffAsync();
        var client = await api.SignInAsync(staff);
        var id = (await (await client.PostAsync("/api/documents", Upload(Markdown("Eski"), "sss.md", "SSS"), Cancel))
            .Content.ReadFromJsonAsync<DocumentCreatedResponse>(Cancel))!.Id;
        await DocumentHelpers.WaitForStatusAsync(client, id, "Done");

        var replaced = await client.PutAsync($"/api/documents/{id}/file", FileOnly(Markdown("Yeni metin"), "sss-v2.md"), Cancel);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var document = await DocumentHelpers.WaitForStatusAsync(client, id, "Done", version: 2);

        Assert.Equal("sss-v2.md", document.FileName);
        var contents = await api.WithDbAsync(db => db.Chunks.Where(c => c.DocumentId == id).Select(c => c.Content).ToListAsync(Cancel));
        Assert.All(contents, content => Assert.DoesNotContain("Eski", content));
        Assert.Contains(contents, content => content.Contains("Yeni metin"));
        Assert.True(await AuditedAsync(AuditActions.DocumentReplaced, id, staff.Id));
    }

    [Fact]
    public async Task Deleted_document_and_its_chunks_are_gone()
    {
        var staff = await api.CreateStaffAsync();
        var client = await api.SignInAsync(staff);
        var id = (await (await client.PostAsync("/api/documents", Upload(Markdown("Silinecek"), "sil.md", "Silinecek"), Cancel))
            .Content.ReadFromJsonAsync<DocumentCreatedResponse>(Cancel))!.Id;
        await DocumentHelpers.WaitForStatusAsync(client, id, "Done");

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/documents/{id}", Cancel)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/documents/{id}", Cancel)).StatusCode);
        Assert.Equal(0, await api.WithDbAsync(db => db.Chunks.CountAsync(c => c.DocumentId == id, Cancel)));
        Assert.True(await AuditedAsync(AuditActions.DocumentDeleted, id, staff.Id));
    }

    [Fact]
    public async Task Download_returns_the_uploaded_bytes()
    {
        var client = await api.SignInAsNewAsync();
        var content = Markdown("İndirilecek");
        var id = (await (await client.PostAsync("/api/documents", Upload(content, "indir.md", "İndir"), Cancel))
            .Content.ReadFromJsonAsync<DocumentCreatedResponse>(Cancel))!.Id;

        var downloaded = await client.GetByteArrayAsync($"/api/documents/{id}/file", Cancel);

        Assert.Equal(content, downloaded);
    }

    // The AI service is down: the document waits, the attempt is not counted, and staff
    // see why.
    [Fact]
    public async Task Document_waits_while_the_ai_service_is_down()
    {
        var client = await api.SignInAsNewAsync();
        api.Embeddings.Unavailable = true;
        try
        {
            var id = (await (await client.PostAsync("/api/documents", Upload(Markdown("Bekleyen"), "bekle.md", "Bekleyen"), Cancel))
                .Content.ReadFromJsonAsync<DocumentCreatedResponse>(Cancel))!.Id;

            var document = await DocumentHelpers.WaitUntilAsync(client, id, d => d.Status == "Pending" && d.FailureReason is not null);

            Assert.Equal(DocumentIngestionHandler.AiServiceUnavailableMessage, document.FailureReason);
            Assert.Equal(0, document.AttemptCount);
            Assert.Equal(0, document.ChunkCount);
        }
        finally
        {
            api.Embeddings.Unavailable = false;
        }
    }

    private Task<bool> AuditedAsync(string action, Guid documentId, Guid userId) =>
        api.WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == action && a.EntityId == documentId && a.UserId == userId, Cancel));

    // A Markdown document of two sections; the marker makes each one's content unique.
    private static byte[] Markdown(string marker) => Encoding.UTF8.GetBytes(
        $"# Bölüm 1\n\n{marker}: öğrenci işletmede on altı hafta çalışır. {Guid.NewGuid()}\n\n# Bölüm 2\n\n{marker}: not ortalaması en az 2,00 olmalıdır.\n");

    private static MultipartFormDataContent Upload(
        byte[] content, string fileName, string title, string unit = "İMEP Koordinatörlüğü", string kind = "Regulation", string? sourceUrl = null)
    {
        var form = FileOnly(content, fileName);
        form.Add(new StringContent(title), "title");
        form.Add(new StringContent(unit), "unit");
        form.Add(new StringContent(kind), "kind");
        if (sourceUrl is not null)
        {
            form.Add(new StringContent(sourceUrl), "sourceUrl");
        }

        return form;
    }

    private static MultipartFormDataContent FileOnly(byte[] content, string fileName)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private sealed record ValidationErrors(Dictionary<string, string[]> Errors);
}

internal static class DocumentHelpers
{
    // Processing runs in the background; the test waits for it like the staff screen does.
    public static Task<DocumentDetailResponse> WaitForStatusAsync(HttpClient client, Guid id, string status, int version = 1) =>
        WaitUntilAsync(client, id, d => d.Status == status && d.Version == version);

    public static async Task<DocumentDetailResponse> WaitUntilAsync(HttpClient client, Guid id, Func<DocumentDetailResponse, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var document = await client.GetFromJsonAsync<DocumentDetailResponse>($"/api/documents/{id}", TestContext.Current.CancellationToken);
            if (done(document!))
            {
                return document!;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Document {id} is still {document!.Status} (version {document.Version}, {document.FailureReason}).");
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}
