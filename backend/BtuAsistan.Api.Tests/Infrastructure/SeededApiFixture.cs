using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BtuAsistan.Api.Modules.Documents;
using BtuAsistan.Api.Tests.Integration;

namespace BtuAsistan.Api.Tests.Infrastructure;

/// <summary>
/// The API with a few small documents uploaded and processed, for the search and chat
/// tests. Each document is about one thing, so which one a question should find is clear.
/// </summary>
public class SeededApiFixture : ApiFixture
{
    public const string Regulation = "İMEP Yönergesi";
    public const string Calendar = "Akademik Takvim";

    public static readonly string RegulationText = """
        # BTÜ İMEP Yönergesi

        ## Sigorta

        İşletmede mesleki eğitim süresince öğrencilerin iş kazası ve meslek hastalığı sigortası Rektörlük tarafından karşılanır.

        ## Başvuru şartları

        Başvuru yapabilmek için genel ağırlıklı not ortalamasının en az 2,00 olması gerekir.
        """;

    public static readonly string CalendarText = """
        # Güz dönemi

        İMEP başvuruları 15 Temmuz ile 25 Ağustos tarihleri arasında yapılır.

        # Bahar dönemi

        Ara faaliyet raporu sekizinci haftanın sonunda teslim edilir.
        """;

    public Guid RegulationId { get; private set; }

    public Guid CalendarId { get; private set; }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        var staff = await SignInAsNewAsync();
        RegulationId = await UploadAsync(staff, Regulation, "İMEP Koordinatörlüğü", "Regulation", RegulationText);
        CalendarId = await UploadAsync(staff, Calendar, "Öğrenci İşleri", "AcademicCalendar", CalendarText, "2026-2027");
        await DocumentHelpers.WaitForStatusAsync(staff, RegulationId, "Done");
        await DocumentHelpers.WaitForStatusAsync(staff, CalendarId, "Done");
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string title, string unit, string kind, string text, string? validityPeriod = null)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        var form = new MultipartFormDataContent
        {
            { file, "file", $"{Guid.NewGuid():N}.md" },
            { new StringContent(title), "title" },
            { new StringContent(unit), "unit" },
            { new StringContent(kind), "kind" },
        };
        if (validityPeriod is not null)
        {
            form.Add(new StringContent(validityPeriod), "validityPeriod");
        }

        var response = await client.PostAsync("/api/documents", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DocumentCreatedResponse>())!.Id;
    }
}
