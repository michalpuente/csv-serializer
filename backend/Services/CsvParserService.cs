using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;

namespace CsvSerializer.Api.Services;

public class ParseResult
{
    public string Encoding { get; set; } = "utf-8";
    public string Delimiter { get; set; } = ";";
    public int HeaderLineIndex { get; set; }
    public List<string> RawHeaders { get; set; } = new();
    public List<string> NormalizedHeaders { get; set; } = new();
    public List<List<string?>> SampleRows { get; set; } = new();
    public List<List<string?>> AllRows { get; set; } = new();
}

public static class CsvParserService
{
    private static readonly string[] Delimiters = [";", ",", "\t"];

    private static readonly string[] BankingKeywords =
        ["data", "kwota", "saldo", "tytul", "opis", "konto", "nadawca", "odbiorca"];

    static CsvParserService()
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string DetectEncoding(byte[] raw)
    {
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            return "utf-8";

        var utf8Text = System.Text.Encoding.UTF8.GetString(raw);
        if (!utf8Text.Contains('\uFFFD') && !HasMojibake(utf8Text))
            return "utf-8";

        string[] candidates = ["windows-1250", "iso-8859-2"];
        var bestEncoding = "windows-1250";
        var bestScore = int.MaxValue;

        foreach (var enc in candidates)
        {
            try
            {
                var text = System.Text.Encoding.GetEncoding(enc).GetString(raw);
                var score = ScoreSuspicion(text);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestEncoding = enc;
                }
            }
            catch { }
        }

        return bestEncoding;
    }

    private static bool HasMojibake(string text) =>
        text.Any(c => "\u0153\u0152\u00A3\u00B3\u00BF\u0178\u00AF\u00E6\u00C6\u00EA\u00CA\u00B9\u00A5\u00F1\u00D1".Contains(c));

    private static int ScoreSuspicion(string text)
    {
        int score = 0;
        foreach (var ch in text)
        {
            if (ch == '\uFFFD') score += 10;
            if ("\u0153\u0152\u00A3\u00B3\u00BF\u0178".Contains(ch)) score += 5;
        }
        return score;
    }

    public static string DetectDelimiter(string[] lines)
    {
        var testLines = lines.Take(40).ToArray();
        var bestDelim = ";";
        var bestScore = 0;

        foreach (var delim in Delimiters)
        {
            var score = testLines.Count(l => l.Split(delim).Length > 2);
            if (score > bestScore)
            {
                bestScore = score;
                bestDelim = delim;
            }
        }

        return bestDelim;
    }

    public static (int lineIndex, string[] headers) DetectHeaderLine(string[] lines, string delimiter)
    {
        for (int i = 0; i < Math.Min(lines.Length, 20); i++)
        {
            var cells = lines[i].Split(delimiter);
            if (cells.Length < 4) continue;

            var hashPrefixed = cells.Count(c => c.TrimStart().StartsWith('#'));
            if (hashPrefixed >= 2) return (i, cells);

            var normalized = cells.Select(c =>
                NormalizationService.RepairPolishText(c.Trim().TrimStart('#').ToLowerInvariant()));
            var keywordMatches = normalized.Count(c => BankingKeywords.Any(k => c.Contains(k)));
            if (keywordMatches >= 2) return (i, cells);
        }

        if (lines.Length > 0)
        {
            var cells = lines[0].Split(delimiter);
            return (0, cells);
        }

        return (0, Array.Empty<string>());
    }

    public static ParseResult Parse(byte[] fileBytes, bool fullParse = false)
    {
        var encoding = DetectEncoding(fileBytes);
        var enc = System.Text.Encoding.GetEncoding(encoding);
        var text = enc.GetString(fileBytes);

        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = text.Split('\n', StringSplitOptions.None)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        var delimiter = DetectDelimiter(lines);
        var (headerLineIndex, rawHeaderCells) = DetectHeaderLine(lines, delimiter);

        var rawHeaders = rawHeaderCells.Select(h => h.Trim()).ToList();
        var normalizedHeaders = SqlIdentifierService.NormalizeHeaders(rawHeaders);

        var dataLines = lines.Skip(headerLineIndex + 1).ToArray();
        var allRows = new List<List<string?>>();

        foreach (var line in dataLines)
        {
            var cells = line.Split(delimiter);
            var normalized = new List<string?>();
            for (int i = 0; i < normalizedHeaders.Count; i++)
            {
                var val = i < cells.Length ? NormalizationService.NormalizeCell(cells[i]) : null;
                normalized.Add(val);
            }
            allRows.Add(normalized);
        }

        return new ParseResult
        {
            Encoding = encoding,
            Delimiter = delimiter,
            HeaderLineIndex = headerLineIndex,
            RawHeaders = rawHeaders,
            NormalizedHeaders = normalizedHeaders,
            SampleRows = allRows.Take(10).ToList(),
            AllRows = fullParse ? allRows : new List<List<string?>>()
        };
    }
}
