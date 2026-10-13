using System.Text;
using CsvSerializer.Api.Services;

namespace CsvSerializer.Tests;

public class CsvParserTests
{
    static CsvParserTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static byte[] Cp1250(string s) => Encoding.GetEncoding(1250).GetBytes(s);

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
    public void DetectDelimiter_IgnoresDelimitersInsideQuotes()
    {
        var text = "a,b,c\n\"x;y;z;w\",2,3\n\"p;q;r;s\",5,6\n";
        Assert.Equal(",", CsvParserService.DetectDelimiter(text));
    }

    [Fact]
    public void Tokenize_HandlesQuotesEscapesAndNewlines()
    {
        var records = CsvParserService.Tokenize("id;text\n1;\"a;b \"\"quoted\"\"\nsecond line\"\n2;plain\n", ';');
        Assert.Equal(3, records.Count);
        Assert.Equal("a;b \"quoted\"\nsecond line", records[1][1]);
        Assert.Equal("plain", records[2][1]);
    }

    [Fact]
    public void Parse_BasicCsv()
    {
        var csv = "name;age;city\nAlice;30;Warsaw\nBob;25;Krakow\n";
        var result = CsvParserService.Parse(Encoding.UTF8.GetBytes(csv));

        Assert.Equal(3, result.NormalizedHeaders.Count);
        Assert.Equal(2, result.SampleRows.Count);
        Assert.Equal("utf-8", result.Encoding);
        Assert.True(result.Columns.Single(c => c.Name == "age").IsNumeric);
    }

    private const string MBankExport = """
        mBank S.A. Bankowość Detaliczna;
        Skrytka Pocztowa 2108;
        90-959 Łódź 2;
        www.mBank.pl;
        mLinia: 801 300 800;
        Z tel. kom. +48 42 6 300 800;
        ;
        #Klient;
        JAN KOWALSKI;
        ;
        Lista operacji;
        ;
        #Za okres:;
        01.01.2018;13.10.2026;
        ;
        #zgodnie z wybranymi filtrami wyszukiwania;
        #dla rachunków:;
        eKonto - 11114020040000300000000000;
        ;
        #Lista nie jest dokumentem w rozumieniu przepisów;
        ;
        #Waluta;#Wpływy;#Wydatki;
        PLN;3 000,00;-35,45;
        ;
        #Data operacji;#Opis operacji;#Rachunek;#Kategoria;#Kwota;
        2026-10-01;"ŻABKA Z1234 K.1    /GLIWICE  ;DATA TRANSAKCJI: 2026-09-30";"eKonto 1111 ... 0000";"Żywność i chemia domowa";-12,30 PLN;
        2026-10-01;"OPŁATA ZA KARTĘ";"eKonto 1111 ... 0000";"Opłaty i odsetki";-5,00 PLN;
        2026-10-01;"OPŁATA ZA KARTĘ";"eKonto 1111 ... 0000";"Opłaty i odsetki";-5,00 PLN;
        2026-10-02;"PRZELEW PRZYCHODZĄCY";"eKonto 1111 ... 0000";"Wpływy";3 000,00 PLN;
        2026-10-03;"ORLEN /RUDA ŚLĄSKA";"eKonto 1111 ... 0000";"Paliwo";-18,15 PLN;
        2026-10-04;"BIEDRONKA";"eKonto 1111 ... 0000";"Żywność i chemia domowa";-1,00 PLN;
        2026-10-05;"BIEDRONKA";"eKonto 1111 ... 0000";"Żywność i chemia domowa";-2,00 PLN;
        2026-10-06;"BIEDRONKA";"eKonto 1111 ... 0000";"Żywność i chemia domowa";-3,00 PLN;
        2026-10-07;"BIEDRONKA";"eKonto 1111 ... 0000";"Żywność i chemia domowa";-4,00 PLN;
        2026-10-08;"BIEDRONKA";"eKonto 1111 ... 0000";"Żywność i chemia domowa";-5,00 PLN;
        ;;;#Saldo końcowe;2 959,55 PLN;
        """;

    [Fact]
    public void Parse_BankExport_SkipsPreambleAndFooter()
    {
        var result = CsvParserService.Parse(Cp1250(MBankExport), fullParse: true);

        Assert.Equal("windows-1250", result.Encoding);
        Assert.Equal(";", result.Delimiter);
        Assert.Equal(["data_operacji", "opis_operacji", "rachunek", "kategoria", "kwota"], result.NormalizedHeaders);
        Assert.Equal(10, result.AllRows.Count);
        Assert.Equal(1, result.SkippedTrailingRecords);

        // quoted delimiter stays inside the field, whitespace padding collapsed
        Assert.Equal("ŻABKA Z1234 K.1 /GLIWICE ;DATA TRANSAKCJI: 2026-09-30", result.AllRows[0][1]);
        Assert.Equal("Żywność i chemia domowa", result.AllRows[0][3]);

        var kwota = result.Columns.Single(c => c.Name == "kwota");
        Assert.True(kwota.IsNumeric);
        Assert.Equal(3000.00m, NormalizationService.TryParseAmount(result.AllRows[3][4], kwota.DecimalSeparator));
        Assert.True(result.Columns.Single(c => c.Name == "data_operacji").IsDate);
    }

    [Fact]
    public void Parse_CommaCsvWithBomAndLongIdentifiers()
    {
        var csv = "﻿Dostawca,Data transakcji,Nr karty,Ilość,Cena\n" +
                  "MOYA,2026-10-08 18:14,7776950000003643531,38.05,262.82\n" +
                  "ORLEN,2026-10-06 17:34,78971518040055867,43.11,303.37\n" +
                  "FLOTEX,2026-09-30 23:59,7002376000645322343,1,1.23\n";
        var result = CsvParserService.Parse(Encoding.UTF8.GetBytes(csv), fullParse: true);

        Assert.Equal(",", result.Delimiter);
        Assert.Equal(3, result.AllRows.Count);
        var cols = result.Columns.ToDictionary(c => c.Name);
        Assert.True(cols["data_transakcji"].IsDate);
        Assert.False(cols["nr_karty"].IsNumeric); // card numbers are identifiers, not amounts
        Assert.True(cols["ilosc"].IsNumeric);
        Assert.Equal('.', cols["cena"].DecimalSeparator);
    }

    [Fact]
    public void Parse_HeaderWiderThanData_KeepsAllColumns()
    {
        var csv = "Dostawca,Data,Ilość,Licznik,Notatka,MPK\n" +
                  "MOYA,2026-10-08,38.05,,,\n" +
                  "ORLEN,2026-10-06,43.11,,,\n" +
                  "SHELL,2026-10-05,12.00,,,\n";
        var result = CsvParserService.Parse(Encoding.UTF8.GetBytes(csv), fullParse: true);

        Assert.True(result.HasHeader);
        Assert.Equal(["dostawca", "data", "ilosc", "licznik", "notatka", "mpk"], result.NormalizedHeaders);
        Assert.Equal(3, result.AllRows.Count);
    }

    [Fact]
    public void Parse_HeaderlessFile_GeneratesColumnNames()
    {
        var csv = "2026-01-01;100,00;Opis A\n2026-01-02;200,50;Opis B\n2026-01-03;-3,10;Opis C\n";
        var result = CsvParserService.Parse(Encoding.UTF8.GetBytes(csv), fullParse: true);

        Assert.False(result.HasHeader);
        Assert.Equal(["col_1", "col_2", "col_3"], result.NormalizedHeaders);
        Assert.Equal(3, result.AllRows.Count);
    }

    [Fact]
    public void Parse_RepairsMojibakeFileWide_ButLeavesCorrectTextAlone()
    {
        var broken = "Data;Opis;Kwota\n2026-01-01;Bankowoœæ;1,00\n2026-01-02;£ódŸ;2,00\n2026-01-03;Tytu³;3,00\n";
        Assert.Equal("Bankowość", CsvParserService.Parse(Encoding.UTF8.GetBytes(broken)).SampleRows[0][1]);

        var correct = "Data;Opis;Kwota\n2026-01-01;Łódź £ sklep;1,00\n2026-01-02;Café æble;2,00\n";
        Assert.Equal("Café æble", CsvParserService.Parse(Encoding.UTF8.GetBytes(correct)).SampleRows[1][1]);
    }

    [Fact]
    public void Parse_KeepsRowsWithMoreFieldsThanHeader()
    {
        var csv = "a;b;c\n1;2;3\n4;5;6;7\n8;9;10\n";
        var result = CsvParserService.Parse(Encoding.UTF8.GetBytes(csv), fullParse: true);
        Assert.Equal(3, result.AllRows.Count);
        Assert.Equal("6 | 7", result.AllRows[1][2]);
        Assert.Equal(1, result.MismatchedRows);
    }

    [Fact]
    public void RowHash_DistinguishesRepeatedIdenticalRows()
    {
        var row = new List<string?> { "2026-10-01", "OPŁATA", "-5,00" };
        Assert.NotEqual(NormalizationService.ComputeRowHash(row), NormalizationService.ComputeRowHash(row, 1));
    }
}
