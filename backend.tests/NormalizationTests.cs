using CsvSerializer.Api.Services;

namespace CsvSerializer.Tests;

public class NormalizationTests
{
    [Theory]
    [InlineData("Bankowoœæ", "Bankowość")]
    [InlineData("£ódŸ", "Łódź")]
    [InlineData("MICHA£", "MICHAŁ")]
    [InlineData("nastêpnej", "następnej")]
    [InlineData("Tytu³", "Tytuł")]
    public void RepairPolishText_FixesMojibake(string input, string expected)
    {
        var result = NormalizationService.RepairPolishText(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeCell_ReturnsNull_ForEmpty()
    {
        Assert.Null(NormalizationService.NormalizeCell(""));
        Assert.Null(NormalizationService.NormalizeCell("  "));
        Assert.Null(NormalizationService.NormalizeCell(null));
    }

    [Theory]
    [InlineData("01.03.2026", "2026-03-01")]
    [InlineData("2026-03-01", "2026-03-01")]
    [InlineData("15/06/2025", "2025-06-15")]
    [InlineData("2026-10-09 20:45", "2026-10-09")]
    [InlineData("1.3.2026", "2026-03-01")]
    public void TryParseDate_ParsesFormats(string input, string expected)
    {
        Assert.Equal(expected, NormalizationService.TryParseDate(input));
    }

    [Theory]
    [InlineData("1 234,56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("-100,00", -100.00)]
    [InlineData("-12,30 PLN", -12.30)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("3\u00A0000,00", 3000.00)]
    [InlineData("(5,00)", -5.00)]
    public void TryParseAmount_ParsesFormats(string input, double expected)
    {
        var result = NormalizationService.TryParseAmount(input);
        Assert.NotNull(result);
        Assert.Equal((decimal)expected, result!.Value);
    }

    [Theory]
    [InlineData("2026-13-45")]
    [InlineData("315")]
    [InlineData("Gliwice")]
    public void TryParseDate_RejectsNonDates(string input)
    {
        Assert.Null(NormalizationService.TryParseDate(input));
    }

    [Fact]
    public void NormalizeCell_StripsSpreadsheetTextWrappers()
    {
        Assert.Equal("33105014031000009123983851", NormalizationService.NormalizeCell("'33105014031000009123983851'"));
        Assert.Null(NormalizationService.NormalizeCell("''"));
        Assert.Equal("A B", NormalizationService.NormalizeCell("  A      B  "));
    }
}
