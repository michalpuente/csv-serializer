using System.Text;

namespace CsvSerializer.Api.Services;

public class ParseResult
{
    public string Encoding { get; set; } = "utf-8";
    public string Delimiter { get; set; } = ";";
    public int HeaderLineIndex { get; set; }
    public bool HasHeader { get; set; } = true;
    public bool MojibakeRepaired { get; set; }
    public int SkippedLeadingRecords { get; set; }
    public int SkippedTrailingRecords { get; set; }
    public int MismatchedRows { get; set; }
    public List<string> RawHeaders { get; set; } = new();
    public List<string> NormalizedHeaders { get; set; } = new();
    public List<ColumnProfile> Columns { get; set; } = new();
    public List<List<string?>> SampleRows { get; set; } = new();
    public List<List<string?>> AllRows { get; set; } = new();
}

/// <summary>
/// Generic CSV reader: no assumptions about the source (bank exports, ERP dumps, spreadsheets).
/// Detects encoding, delimiter and where the actual table starts/ends, so preambles and
/// footers (e.g. account summaries in bank exports) are skipped instead of becoming columns.
/// </summary>
public static class CsvParserService
{
    private static readonly char[] Delimiters = [';', ',', '\t', '|'];
    private const int SampleRecords = 200;

    static CsvParserService()
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string DetectEncoding(byte[] raw)
    {
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) return "utf-8";
        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE) return "utf-16";
        if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF) return "utf-16BE";

        // Strict decode: any invalid byte sequence means this is not UTF-8.
        try
        {
            new UTF8Encoding(false, true).GetString(raw);
            return "utf-8";
        }
        catch (DecoderFallbackException) { }

        // Single-byte legacy code pages: pick the one that yields the most letters and the fewest oddities.
        string[] candidates = ["windows-1250", "iso-8859-2", "windows-1252"];
        return candidates
            .Select(enc => (enc, score: ScoreLegacyDecoding(System.Text.Encoding.GetEncoding(enc).GetString(raw))))
            .OrderByDescending(x => x.score)
            .First().enc;
    }

    private static int ScoreLegacyDecoding(string text)
    {
        int score = 0;
        foreach (var ch in text)
        {
            if (ch < 0x80) continue;
            if (char.IsLetter(ch)) score += 2;
            else if (char.IsControl(ch) || ch == '�') score -= 5;
            else score -= 1;
        }
        return score;
    }

    /// <summary>Splits text into records, honouring quotes (embedded delimiters, doubled quotes, newlines).</summary>
    public static List<string[]> Tokenize(string text, char delimiter)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false, fieldStarted = false, wasQuoted = false;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            fieldStarted = wasQuoted = false;
        }

        void EndRecord()
        {
            EndField();
            records.Add(fields.ToArray());
            fields.Clear();
        }

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            if (c == '"' && !fieldStarted && !wasQuoted && field.ToString().Trim().Length == 0)
            {
                field.Clear();
                inQuotes = true;
                wasQuoted = true;
                continue;
            }

            if (c == delimiter) { EndField(); continue; }
            if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; EndRecord(); continue; }
            if (c == '\n') { EndRecord(); continue; }

            // Text after a closing quote (malformed CSV) is kept rather than dropped.
            if (!char.IsWhiteSpace(c)) fieldStarted = true;
            field.Append(c);
        }

        if (field.Length > 0 || fields.Count > 0 || wasQuoted) EndRecord();

        return records.Where(r => !IsBlank(r)).ToList();
    }

    private static bool IsBlank(string[] record) => record.All(string.IsNullOrWhiteSpace);

    /// <summary>Picks the delimiter whose records most consistently share one width of at least two fields.</summary>
    public static string DetectDelimiter(string text)
    {
        var sample = text.Length > 200_000 ? text[..200_000] : text;
        var best = (delim: ';', score: -1);

        foreach (var delim in Delimiters)
        {
            var records = Tokenize(sample, delim).Take(SampleRecords).ToList();
            var (width, count) = DominantWidth(records);
            if (width < 2) continue;
            // Consistency counts most; wider tables break ties (a stray comma in text yields width 2).
            var score = count * 100 + width;
            if (score > best.score) best = (delim, score);
        }

        return best.delim.ToString();
    }

    public static string DetectDelimiter(string[] lines) => DetectDelimiter(string.Join('\n', lines));

    private static (int width, int count) DominantWidth(IEnumerable<string[]> records)
    {
        var top = records
            .Select(EffectiveWidth)
            .Where(w => w >= 2)
            .GroupBy(w => w)
            .Select(g => (width: g.Key, count: g.Count()))
            .OrderByDescending(x => x.count).ThenByDescending(x => x.width)
            .FirstOrDefault();
        return top;
    }

    /// <summary>Width ignoring trailing empty cells (many exports end every line with a delimiter).</summary>
    private static int EffectiveWidth(string[] record)
    {
        var w = record.Length;
        while (w > 0 && string.IsNullOrWhiteSpace(record[w - 1])) w--;
        return w;
    }

    /// <summary>
    /// Finds the table inside the file: the header is the first record that looks like labels and is
    /// followed by data of the dominant width. Returns -1 when the table has no header row.
    /// </summary>
    public static (int headerIndex, int width) DetectTable(List<string[]> records)
    {
        if (records.Count == 0) return (0, 0);

        var (width, _) = DominantWidth(records);
        if (width == 0) return (0, Math.Max(1, EffectiveWidth(records[0]))); // single-column file

        int minWidth = MinTableWidth(width);
        bool Fits(int idx) => EffectiveWidth(records[idx]) is var w && w >= minWidth && w <= width;
        // A table row must be followed by more rows of the same shape (preamble lines rarely are).
        bool FollowedByData(int idx)
        {
            int ahead = Math.Min(3, records.Count - idx - 1);
            return ahead > 0 && Enumerable.Range(idx + 1, ahead).All(j => EffectiveWidth(records[j]) >= minWidth);
        }

        for (int i = 0; i < records.Count; i++)
        {
            var w = EffectiveWidth(records[i]);
            if (w < minWidth || !FollowedByData(i)) continue;
            // Headers may name trailing columns that are always empty in the data, so they can be wider.
            if (LooksLikeHeader(records[i], w)) return (i, Math.Max(w, width));
            if (w <= width) return (-1 - i, width);
        }

        // Tiny files (header + one row): fall back to the first record of the dominant width.
        var first = Enumerable.Range(0, records.Count).First(Fits);
        return LooksLikeHeader(records[first], width) ? (first, width) : (-1 - first, width);
    }

    /// <summary>Minimum width a record needs to count as part of the table (short rows are tolerated).</summary>
    private static int MinTableWidth(int width) => width < 2 ? 1 : Math.Max(2, (int)Math.Ceiling(width * 0.75));

    private static bool LooksLikeHeader(string[] record, int width)
    {
        var cells = record.Take(width).Select(c => c.Trim()).ToList();
        var nonEmpty = cells.Where(c => c.Length > 0).ToList();
        if (nonEmpty.Count < Math.Max(2, cells.Count / 2)) return false;
        if (nonEmpty.Distinct(StringComparer.OrdinalIgnoreCase).Count() < nonEmpty.Count * 0.8) return false;

        // Labels are text; data rows usually carry dates and numbers.
        var valueLike = nonEmpty.Count(c =>
            NormalizationService.TryParseDate(NormalizationService.NormalizeCell(c)) != null ||
            NormalizationService.TryParseAmount(NormalizationService.NormalizeCell(c)) != null);
        return valueLike <= nonEmpty.Count / 4;
    }

    public static ParseResult Parse(byte[] fileBytes, bool fullParse = false)
    {
        var encoding = DetectEncoding(fileBytes);
        var text = System.Text.Encoding.GetEncoding(encoding).GetString(fileBytes).TrimStart('﻿');

        // Text decoded once with the wrong code page and saved again (e.g. "Bankowoœæ") is repaired file-wide.
        var mojibake = NormalizationService.LooksLikeMojibake(text);
        if (mojibake) text = NormalizationService.RepairPolishText(text, collapseWhitespace: false);

        var delimiter = DetectDelimiter(text);
        var records = Tokenize(text, delimiter[0]);
        var (headerIndex, width) = DetectTable(records);

        bool hasHeader = headerIndex >= 0;
        int firstDataIndex = hasHeader ? headerIndex + 1 : -1 - headerIndex;

        List<string> rawHeaders = hasHeader
            ? records[headerIndex].Take(width).Select(h => h.Trim()).ToList()
            : Enumerable.Range(1, width).Select(i => $"col_{i}").ToList();

        // Data ends at the last record that still has the table's shape; anything after is a footer.
        int minWidth = MinTableWidth(DominantWidth(records).width);
        int lastDataIndex = records.Count - 1;
        while (lastDataIndex >= firstDataIndex && EffectiveWidth(records[lastDataIndex]) < minWidth) lastDataIndex--;

        // Summary lines shaped like the table (";;;#Saldo końcowe;1 234,56;") leave the first column empty,
        // which real rows practically never do.
        int footer = 0;
        while (lastDataIndex - footer >= firstDataIndex && string.IsNullOrWhiteSpace(records[lastDataIndex - footer][0])) footer++;
        int body = lastDataIndex - footer - firstDataIndex + 1;
        if (footer > 0 && footer <= 5 && body >= 5)
        {
            int withFirstCell = 0;
            for (int r = firstDataIndex; r <= lastDataIndex - footer; r++)
                if (!string.IsNullOrWhiteSpace(records[r][0])) withFirstCell++;
            if (withFirstCell >= body * 0.95) lastDataIndex -= footer;
        }

        var allRows = new List<List<string?>>();
        int mismatched = 0;
        for (int r = firstDataIndex; r <= lastDataIndex; r++)
        {
            var cells = records[r];
            var effective = EffectiveWidth(cells);
            if (effective > width || effective < minWidth) mismatched++;

            var row = new List<string?>(width);
            for (int i = 0; i < width; i++)
                row.Add(i < cells.Length ? NormalizationService.NormalizeCell(cells[i]) : null);

            // Never drop data silently: overflow cells are appended to the last column.
            if (effective > width)
                row[width - 1] = string.Join(" | ", new[] { row[width - 1] }
                    .Concat(cells.Skip(width).Take(effective - width).Select(NormalizationService.NormalizeCell))
                    .Where(v => v != null));

            allRows.Add(row);
        }

        var normalizedHeaders = SqlIdentifierService.NormalizeHeaders(rawHeaders);

        return new ParseResult
        {
            Encoding = encoding,
            Delimiter = delimiter,
            HeaderLineIndex = hasHeader ? headerIndex : -1,
            HasHeader = hasHeader,
            MojibakeRepaired = mojibake,
            SkippedLeadingRecords = Math.Max(0, firstDataIndex - (hasHeader ? 1 : 0)),
            SkippedTrailingRecords = Math.Max(0, records.Count - 1 - lastDataIndex),
            MismatchedRows = mismatched,
            RawHeaders = rawHeaders,
            NormalizedHeaders = normalizedHeaders,
            Columns = ColumnProfiler.Profile(normalizedHeaders, allRows),
            SampleRows = allRows.Take(10).ToList(),
            AllRows = fullParse ? allRows : new List<List<string?>>()
        };
    }
}
