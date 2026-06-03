using System.Text;
using CsvSerializer.Api.Services;

namespace CsvSerializer.Tests;

public class CsvParserTests
{
    [Fact]
    public void DetectDelimiter_FindsSemicolon()
    {
        var lines = new[]
        {
            "header1;header2;header3;header4",
            "a;b;c;d",
            "e;f;g;h"
        };
        Assert.Equal(";", CsvParserService.DetectDelimiter(lines));
    }

    [Fact]
    public void DetectDelimiter_FindsComma()
    {
        var lines = new[]
        {
            "header1,header2,header3,header4",
            "a,b,c,d"
        };
        Assert.Equal(",", CsvParserService.DetectDelimiter(lines));
    }

    [Fact]
    public void DetectHeaderLine_FindsBankingHeaders()
    {
        var lines = new[]
        {
            "Some preamble text",
            "#Data;#Kwota;#Saldo;#Opis transakcji",
            "2026-01-01;100;500;Test"
        };
        var (index, _) = CsvParserService.DetectHeaderLine(lines, ";");
        Assert.Equal(1, index);
    }

    [Fact]
    public void Parse_BasicCsv()
    {
        var csv = "name;age;city\nAlice;30;Warsaw\nBob;25;Krakow\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var result = CsvParserService.Parse(bytes);

        Assert.Equal(3, result.NormalizedHeaders.Count);
        Assert.Equal(2, result.SampleRows.Count);
        Assert.Equal("utf-8", result.Encoding);
    }
}
