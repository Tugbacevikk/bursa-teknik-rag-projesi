using System.Net;
using System.Net.Http.Json;
using BtuAsistan.Api.Modules.Search;
using BtuAsistan.Api.Tests.Infrastructure;

namespace BtuAsistan.Api.Tests.Integration;

// Faz 3: keyword + vector search fused with RRF (PLANNING S1–S6), through the staff search.
public sealed class SearchTests(SeededApiFixture api) : IClassFixture<SeededApiFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<SearchResponse> SearchAsync(string question, string? method = null, int? resultCount = null)
    {
        var client = await api.SignInAsNewAsync();
        var response = await client.PostAsJsonAsync("/api/search", new { question, method, resultCount }, Cancel);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(Cancel))!;
    }

    [Fact]
    public async Task Hybrid_search_finds_the_chunk_both_searches_agree_on()
    {
        var result = await SearchAsync("İş kazası sigortasını kim karşılar?");

        var top = result.Hits[0];
        Assert.Equal(SeededApiFixture.Regulation, top.DocumentTitle);
        Assert.Equal("Sigorta", top.SectionPath);
        Assert.Equal(1, top.KeywordRank);
        Assert.NotNull(top.VectorRank);
        Assert.False(result.VectorSearchFailed);
        Assert.False(result.Reranked);
    }

    // The text shown to staff is what the document says, without the context header.
    [Fact]
    public async Task Hits_show_the_text_without_the_header_and_the_period()
    {
        var result = await SearchAsync("İMEP başvuruları hangi tarihlerde?");

        var hit = result.Hits.First(h => h.DocumentTitle == SeededApiFixture.Calendar);
        Assert.StartsWith("İMEP başvuruları 15 Temmuz", hit.Text);
        Assert.Equal("2026-2027", hit.ValidityPeriod);
    }

    // The turkish dictionary of PostgreSQL: "başvurular" (question) and "başvuruları" (text)
    // share the stem "başvuru".
    [Fact]
    public async Task Keyword_search_matches_another_form_with_the_same_stem()
    {
        var result = await SearchAsync("başvurular", method: "Keyword");

        Assert.Equal(SeededApiFixture.Calendar, result.Hits[0].DocumentTitle);
        Assert.All(result.Hits, hit => Assert.Null(hit.VectorRank));
    }

    // A known limit, found by these tests (2026-11-20): the Snowball stemmer does not bring
    // every form of a word to one stem ("başvurusu" → "başvurus" but "başvuru" → "başvur";
    // "kaza" → "kaz" but "kazası" → "kazas"; "ortalama" → "ortala" but "ortalamasının" →
    // "ortalama"), so keyword search misses these forms of words in the text; vector search
    // still finds them, which is why the chat uses both. Search:KeywordPrefixLength would
    // find them (KeywordPrefixTests) but did not improve the measured ranking, so it is off
    // (PLANNING Testler, step 4).
    [Theory]
    [InlineData("başvurusu")]
    [InlineData("kaza")]
    [InlineData("ortalama")]
    public async Task Keyword_search_alone_misses_some_forms_of_a_word(string word)
    {
        Assert.Empty((await SearchAsync(word, method: "Keyword")).Hits);
        Assert.NotEmpty((await SearchAsync(word)).Hits);
    }

    [Fact]
    public async Task Vector_search_alone_ranks_every_chunk()
    {
        var result = await SearchAsync("sekizinci hafta rapor", method: "Vector", resultCount: 50);

        Assert.Equal("Bahar dönemi", result.Hits[0].SectionPath);
        Assert.All(result.Hits, hit => Assert.Null(hit.KeywordRank));
    }

    [Fact]
    public async Task Result_count_limits_the_hits()
    {
        Assert.Single((await SearchAsync("öğrenci", resultCount: 1)).Hits);
    }

    // The AI service is down: search goes on with keywords alone, and says so.
    [Fact]
    public async Task Keyword_results_still_come_when_the_ai_service_is_down()
    {
        api.Embeddings.Unavailable = true;
        try
        {
            var result = await SearchAsync("sigorta");

            Assert.True(result.VectorSearchFailed);
            Assert.Equal("Sigorta", result.Hits[0].SectionPath);
        }
        finally
        {
            api.Embeddings.Unavailable = false;
        }
    }

    [Theory]
    [InlineData("", null, null, "question")]
    [InlineData("soru", "2", null, "method")]
    [InlineData("soru", "Magic", null, "method")]
    [InlineData("soru", null, 0, "resultCount")]
    [InlineData("soru", null, 51, "resultCount")]
    public async Task Invalid_search_is_refused(string question, string? method, int? resultCount, string field)
    {
        var client = await api.SignInAsNewAsync();

        var response = await client.PostAsJsonAsync("/api/search", new { question, method, resultCount }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"\"{field}\"", await response.Content.ReadAsStringAsync(Cancel));
    }

    // Students reach search only through the chat (S6).
    [Fact]
    public async Task Anonymous_search_is_refused()
    {
        var response = await api.Anonymous().PostAsJsonAsync("/api/search", new { question = "sigorta" }, Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

// Search:KeywordPrefixLength turned on (off by default): the forms exact stems miss are found,
// and an exact match still ranks first.
public sealed class KeywordPrefixTests(KeywordPrefixTests.Fixture api) : IClassFixture<KeywordPrefixTests.Fixture>
{
    private async Task<SearchResponse> KeywordSearchAsync(string question)
    {
        var client = await api.SignInAsNewAsync();
        var response = await client.PostAsJsonAsync("/api/search", new { question, method = "Keyword" }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(TestContext.Current.CancellationToken))!;
    }

    [Theory]
    [InlineData("başvurusu", "Başvuru şartları")]
    [InlineData("kaza", "Sigorta")]
    [InlineData("ortalama", "Başvuru şartları")]
    public async Task Forms_with_another_stem_are_found(string word, string section)
    {
        var hits = (await KeywordSearchAsync(word)).Hits;

        Assert.Contains(hits, hit => hit.SectionPath == section);
    }

    [Fact]
    public async Task Exact_match_ranks_above_a_prefix_only_match()
    {
        // "raporu" → "rapor": exact in "Ara faaliyet raporu"; no other chunk has "rapor…".
        Assert.Equal("Bahar dönemi", (await KeywordSearchAsync("raporu")).Hits[0].SectionPath);
    }

    public sealed class Fixture : SeededApiFixture
    {
        protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
        {
            ["Search:KeywordPrefixLength"] = "5",
        };
    }
}
