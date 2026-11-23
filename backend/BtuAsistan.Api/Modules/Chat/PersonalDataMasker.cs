using System.Text.RegularExpressions;

namespace BtuAsistan.Api.Modules.Chat;

// Replaces Turkish ID numbers, e-mail addresses and phone numbers in a question before it
// is sent to the chat model or stored (PLANNING C10). Best effort: it catches what students
// commonly paste, not every way of writing them; the notice on the chat screen still asks
// them not to write personal data.
public static partial class PersonalDataMasker
{
    public const string IdNumberMask = "[TC kimlik no]";
    public const string EmailMask = "[e-posta]";
    public const string PhoneMask = "[telefon]";

    public static string Mask(string text)
    {
        // ID numbers first: an 11-digit ID never starts with 0, while a phone number written
        // with its leading 0 is also 11 digits.
        text = ElevenDigits().Replace(text, m => IsIdNumber(m.Value) ? IdNumberMask : m.Value);
        text = Email().Replace(text, EmailMask);
        text = Phone().Replace(text, PhoneMask);
        return text;
    }

    // T.C. kimlik numarası: 11 digits, the first not 0, the last two check digits. Checking
    // them leaves other 11-digit numbers (e.g. a document number) alone.
    internal static bool IsIdNumber(string digits)
    {
        if (digits.Length != 11 || digits[0] == '0')
        {
            return false;
        }

        var d = digits.Select(c => c - '0').ToArray();
        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        var tenth = ((odd * 7) - even) % 10;
        if (tenth < 0)
        {
            tenth += 10;
        }

        return d[9] == tenth && d[10] == d.Take(10).Sum() % 10;
    }

    [GeneratedRegex(@"(?<!\d)\d{11}(?!\d)")]
    private static partial Regex ElevenDigits();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+")]
    private static partial Regex Email();

    // Mobile: 5xx xxx xx xx, with or without +90 / 0 in front. Landline: area code 2xx–4xx,
    // only with +90 or 0 in front (otherwise too much like other numbers). Spaces, dashes,
    // dots and brackets between the groups.
    [GeneratedRegex(@"(?<![\d+])(?:(?:\+90|0090|0)[\s.-]?)?\(?5\d{2}\)?[\s.-]?\d{3}[\s.-]?\d{2}[\s.-]?\d{2}(?!\d)|(?<![\d+])(?:\+90|0090|0)[\s.-]?\(?[2-4]\d{2}\)?[\s.-]?\d{3}[\s.-]?\d{2}[\s.-]?\d{2}(?!\d)")]
    private static partial Regex Phone();
}
