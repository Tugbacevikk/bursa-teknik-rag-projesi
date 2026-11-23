using BtuAsistan.Api.Modules.Chat;

namespace BtuAsistan.Api.Tests.Unit;

// PLANNING C10: personal data is masked before the question reaches Gemini or the database.
public sealed class PersonalDataMaskerTests
{
    // Both pass the official check digits (10th and 11th).
    [Theory]
    [InlineData("10000000146")]
    [InlineData("12345678950")]
    public void Valid_id_number_is_masked(string id)
    {
        Assert.Equal($"TC'm {PersonalDataMasker.IdNumberMask}, notum?", PersonalDataMasker.Mask($"TC'm {id}, notum?"));
    }

    [Theory]
    // Wrong check digits: a document or student number, left alone.
    [InlineData("12345678901")]
    // Starts with 0: never an ID number.
    [InlineData("01234567890")]
    public void Other_eleven_digit_numbers_are_kept(string number)
    {
        Assert.Equal($"Belge no {number}", PersonalDataMasker.Mask($"Belge no {number}"));
    }

    [Fact]
    public void Id_number_inside_a_longer_number_is_not_masked()
    {
        Assert.Equal("100000001460", PersonalDataMasker.Mask("100000001460"));
    }

    [Theory]
    [InlineData("ali.veli@ogrenci.btu.edu.tr")]
    [InlineData("ayse+imep@gmail.com")]
    public void Email_is_masked(string email)
    {
        Assert.Equal($"Bana {PersonalDataMasker.EmailMask} adresinden yazın.", PersonalDataMasker.Mask($"Bana {email} adresinden yazın."));
    }

    [Theory]
    [InlineData("05321234567")]
    [InlineData("0532 123 45 67")]
    [InlineData("+90 532 123 45 67")]
    [InlineData("0090 532 123 45 67")]
    [InlineData("(532) 123-45-67")]
    [InlineData("532.123.45.67")]
    [InlineData("0224 300 34 34")]
    [InlineData("+90 (224) 300 34 34")]
    public void Phone_number_is_masked(string phone)
    {
        Assert.Equal($"Numaram {PersonalDataMasker.PhoneMask}.", PersonalDataMasker.Mask($"Numaram {phone}."));
    }

    // A landline without its 0 or +90 looks too much like other numbers to touch.
    [Fact]
    public void Landline_without_prefix_is_kept()
    {
        Assert.Equal("Oda 224 300 34 34", PersonalDataMasker.Mask("Oda 224 300 34 34"));
    }

    [Fact]
    public void Ordinary_question_is_unchanged()
    {
        const string question = "2026-2027 güz döneminde İMEP başvurusu için GANO en az 2.50 mi, 3. sınıfta yapılır mı?";

        Assert.Equal(question, PersonalDataMasker.Mask(question));
    }

    [Fact]
    public void Several_kinds_in_one_text_are_all_masked()
    {
        var masked = PersonalDataMasker.Mask("TC 10000000146, tel 0532 123 45 67, e-posta a@b.com");

        Assert.Equal(
            $"TC {PersonalDataMasker.IdNumberMask}, tel {PersonalDataMasker.PhoneMask}, e-posta {PersonalDataMasker.EmailMask}",
            masked);
    }

    [Theory]
    [InlineData("10000000146", true)]
    [InlineData("10000000147", false)]
    [InlineData("1000000014", false)]
    public void Id_number_check_digits(string digits, bool valid)
    {
        Assert.Equal(valid, PersonalDataMasker.IsIdNumber(digits));
    }
}
