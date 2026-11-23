using BtuAsistan.Api.Modules.Ingestion.Chunking;
using BtuAsistan.Api.Modules.Ingestion.Parsing;

namespace BtuAsistan.Api.Tests.Unit;

public sealed class TurkishSentenceSplitterTests
{
    [Fact]
    public void Splits_at_sentence_ends()
    {
        Assert.Equal(
            ["Başvuru Temmuz'da başlar.", "Son gün ne zaman?", "Ağustos'un sonu!"],
            TurkishSentenceSplitter.Split("Başvuru Temmuz'da başlar. Son gün ne zaman? Ağustos'un sonu!"));
    }

    [Theory]
    // Title, ordinal number, dotted abbreviation, "vb." in running text.
    [InlineData("Dr. Ayşe Kaya dersi verir.")]
    [InlineData("Öğrenci 3. madde uyarınca bildirir.")]
    [InlineData("Staj X A.Ş. bünyesinde yapılır.")]
    [InlineData("Form, dilekçe vb. belgeler istenir.")]
    public void Does_not_split_inside_a_sentence(string sentence)
    {
        Assert.Equal([sentence], TurkishSentenceSplitter.Split(sentence));
    }

    // Nothing is lost: joining the sentences gives the text back.
    [Fact]
    public void Joining_the_sentences_gives_the_text_back()
    {
        const string text = "Madde 5. Öğrenci, md. 3'e göre başvurur. Prof. Dr. Ali Veli onaylar... Sonra ne olur?! Sonuç bildirilir.";

        Assert.Equal(text, string.Join(" ", TurkishSentenceSplitter.Split(text)));
    }
}

public sealed class DocumentChunkerTests
{
    private static ParsedDocument Document(params DocumentBlock[] blocks) => new(blocks);

    [Fact]
    public void Header_names_the_document_its_period_and_section()
    {
        var chunks = DocumentChunker.Chunk("Akademik Takvim", "2026-2027", Document(
            new HeadingBlock("Güz Dönemi", 1, null),
            new HeadingBlock("İMEP", 2, null),
            new ParagraphBlock("Başvurular 15 Temmuz'da başlar.", null),
            new HeadingBlock("Bahar Dönemi", 1, null),
            new ParagraphBlock("Başvurular 15 Aralık'ta başlar.", null)));

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Belge: Akademik Takvim (2026-2027) > Güz Dönemi > İMEP\n\nBaşvurular 15 Temmuz'da başlar.", chunks[0].Content);
        Assert.Equal("Güz Dönemi > İMEP", chunks[0].SectionPath);
        Assert.Equal("Bahar Dönemi", chunks[1].SectionPath);
        Assert.Equal([0, 1], chunks.Select(c => c.Ordinal));
    }

    // A document opening with its only level-1 heading is naming itself.
    [Fact]
    public void Title_heading_is_not_a_section()
    {
        var chunks = DocumentChunker.Chunk("İMEP Yönergesi", null, Document(
            new HeadingBlock("BTÜ İMEP YÖNERGESİ", 1, null),
            new HeadingBlock("Amaç", 2, null),
            new ParagraphBlock("Bu yönergenin amacı…", null)));

        Assert.Equal("Amaç", Assert.Single(chunks).SectionPath);
    }

    [Fact]
    public void Heading_repeating_the_title_is_left_out_of_the_path()
    {
        var chunks = DocumentChunker.Chunk("İmep Yönergesi", null, Document(
            new HeadingBlock("Giriş", 1, null),
            new ParagraphBlock("Metin.", null),
            new HeadingBlock("İMEP YÖNERGESİ", 1, null),
            new ParagraphBlock("Başka metin.", null)));

        Assert.Equal(["Giriş", null], chunks.Select(c => c.SectionPath));
    }

    [Fact]
    public void Long_paragraph_is_split_and_nothing_is_lost()
    {
        var sentence = "Öğrenci işletmede on altı hafta boyunca tam zamanlı çalışır ve her hafta rapor yazar. ";
        var paragraph = string.Concat(Enumerable.Repeat(sentence, 60)).Trim();

        var chunks = DocumentChunker.Chunk("SSS", null, Document(new ParagraphBlock(paragraph, null)));

        Assert.True(chunks.Count > 1);
        var texts = chunks.Select(c => DocumentChunker.WithoutContextHeader(c.Content)).ToList();
        Assert.All(texts, text => Assert.True(text.Length <= DocumentChunker.MaxTextLength, $"{text.Length} characters"));
        Assert.Equal(paragraph, string.Join(" ", texts));
    }

    [Fact]
    public void Table_rows_carry_their_column_names()
    {
        var chunks = DocumentChunker.Chunk("Takvim", null, Document(
            new TableBlock([["Etkinlik", "Tarih", "Not"], ["Başvuru", "15 Temmuz", ""], ["Sonuç", "1 Eylül", "e-posta ile"]], HasHeaderRow: true, PageNumber: 2)));

        var chunk = Assert.Single(chunks);
        Assert.Equal(
            "Etkinlik: Başvuru; Tarih: 15 Temmuz\nEtkinlik: Sonuç; Tarih: 1 Eylül; Not: e-posta ile",
            DocumentChunker.WithoutContextHeader(chunk.Content));
        Assert.Equal(2, chunk.PageNumber);
    }

    [Fact]
    public void Text_without_a_header_is_returned_as_is()
    {
        Assert.Equal("Düz metin", DocumentChunker.WithoutContextHeader("Düz metin"));
    }
}
