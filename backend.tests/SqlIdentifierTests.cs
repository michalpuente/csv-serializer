using CsvSerializer.Api.Services;

namespace CsvSerializer.Tests;

public class SqlIdentifierTests
{
    [Theory]
    [InlineData("#Data księgowania", "data_ksiegowania")]
    [InlineData("#Nadawca/Odbiorca", "nadawca_odbiorca")]
    public void NormalizeIdentifier_NormalizesPolishHeaders(string input, string expected)
    {
        var cleaned = input.TrimStart('#').Trim();
        Assert.Equal(expected, SqlIdentifierService.NormalizeIdentifier(cleaned));
    }

    [Fact]
    public void NormalizeHeaders_DeduplicatesNames()
    {
        var result = SqlIdentifierService.NormalizeHeaders(new[] { "kolumna", "kolumna" });
        Assert.Equal(new[] { "kolumna", "kolumna_2" }, result);
    }

    [Fact]
    public void NormalizeTableName_NormalizesAndValidates()
    {
        Assert.Equal("moja_tabela_2026", SqlIdentifierService.NormalizeTableName(" Moja Tabela 2026 "));
    }

    [Fact]
    public void NormalizeTableName_ThrowsOnEmpty()
    {
        Assert.Throws<ArgumentException>(() => SqlIdentifierService.NormalizeTableName(""));
    }
}
