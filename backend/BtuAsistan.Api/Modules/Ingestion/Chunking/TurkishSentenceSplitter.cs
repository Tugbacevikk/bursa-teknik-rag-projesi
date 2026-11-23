using System.Globalization;

namespace BtuAsistan.Api.Modules.Ingestion.Chunking;

// Adapted from DocAssistant.
//
// Splits a paragraph into sentences for chunks that would otherwise exceed the size limit
// (PLANNING P5). Rule based: a sentence ends at '.', '!', '?' or '…' followed by
// whitespace and something that can start a sentence. A period does not end a sentence
// after a known abbreviation ("Dr.", "vb.", "md."), an initial ("A. Yılmaz"), a dotted
// abbreviation ("A.Ş.", "T.C.") or an ordinal number ("3. madde", "2. Kat").
//
// Getting a split wrong only moves a chunk boundary; nothing is lost, because joining the
// returned sentences with single spaces gives back the text (whitespace aside).
public static class TurkishSentenceSplitter
{
    // Compared case-insensitively with Turkish rules (i/İ and ı/I are different letters).
    private static readonly HashSet<string> Abbreviations = new(
        [
            // Titles
            "Dr", "Prof", "Doç", "Yrd", "Öğr", "Gör", "Uzm", "Op", "Av", "Müh", "Sn", "Hz",
            // Common in running text
            "vb", "vs", "vd", "bkz", "örn", "yakl", "krş", "sf", "sy", "no", "nr", "tel", "yy",
            // Legal texts: madde, fıkra, bent, sayı
            "md", "mad", "fık", "bnt", "sa",
            // Organisations and addresses
            "Ltd", "Şti", "Koll", "Mah", "Cad", "Sok", "Bul", "Apt", "Blv",

            // Month abbreviations ("Oca.", "Ara.") are left out on purpose: several are also
            // ordinary words ("ara", "kas") that can end a sentence.
        ],
        StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true));

    public static IReadOnlyList<string> Split(string text)
    {
        var sentences = new List<string>();
        var start = 0;
        var i = 0;

        while (i < text.Length)
        {
            if (!IsTerminator(text[i]))
            {
                i++;
                continue;
            }

            // Take repeated terminators and closing marks along: "?!", "...", "vb.)".
            var end = i + 1;
            while (end < text.Length && (IsTerminator(text[end]) || IsCloser(text[end])))
            {
                end++;
            }

            if (end < text.Length
                && char.IsWhiteSpace(text[end])
                && !(text[i] == '.' && IsNonFinalPeriod(text, i))
                && CanStartSentence(text, end))
            {
                AddTrimmed(sentences, text[start..end]);
                start = end;
            }

            i = end;
        }

        AddTrimmed(sentences, text[start..]);

        return sentences;
    }

    private static bool IsTerminator(char c) => c is '.' or '!' or '?' or '…';

    private static bool IsCloser(char c) => c is ')' or ']' or '"' or '\'' or '”' or '’' or '»';

    // Looks at the word right before the period at periodIndex.
    private static bool IsNonFinalPeriod(string text, int periodIndex)
    {
        var wordStart = periodIndex;
        while (wordStart > 0 && (char.IsLetterOrDigit(text[wordStart - 1]) || text[wordStart - 1] == '.'))
        {
            wordStart--;
        }

        var word = text[wordStart..periodIndex];

        if (word.Length == 0)
        {
            return false;
        }

        // "A.Ş", "T.C": dotted abbreviations.
        if (word.Contains('.'))
        {
            return true;
        }

        // "3. madde", "2. Kat": Turkish writes ordinals as a number and a period.
        if (word.All(char.IsDigit))
        {
            return true;
        }

        // "A. Yılmaz": an initial.
        if (word.Length == 1 && char.IsLetter(word[0]))
        {
            return true;
        }

        return Abbreviations.Contains(word);
    }

    // After the whitespace that follows a terminator, a new sentence starts with a capital
    // letter, a digit or an opening mark. A lowercase word means the period was not final.
    private static bool CanStartSentence(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index == text.Length)
        {
            return false;
        }

        var c = text[index];

        return char.IsUpper(c) || char.IsDigit(c) || c is '(' or '[' or '"' or '\'' or '“' or '‘' or '«' or '-' or '–' or '•';
    }

    private static void AddTrimmed(List<string> sentences, string sentence)
    {
        var trimmed = sentence.Trim();
        if (trimmed.Length > 0)
        {
            sentences.Add(trimmed);
        }
    }
}
