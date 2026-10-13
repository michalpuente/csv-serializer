using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CsvSerializer.Api.Services;

public static partial class NormalizationService
{
    private static readonly Encoding Cp1252;
    private static readonly Encoding Cp1250;

    // Characters that appear when Central-European text (cp1250) is read as Western (cp1252/latin-1).
    private const string MojibakeMarkers = "œŒ£³¿Ÿ¯æÆêÊ¹¥ñÑ\u009C\u008C\u009F\u008F";
    private const string PolishLetters = "ąćęłńóśźżĄĆĘŁŃÓŚŹŻ";

    static NormalizationService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
        Cp1250 = Encoding.GetEncoding(1250);
    }

    /// <summary>True when the text is cp1250 that was decoded as cp1252 somewhere upstream.</summary>
    public static bool LooksLikeMojibake(string text)
    {
        int markers = 0, polish = 0;
        foreach (var ch in text)
        {
            if (MojibakeMarkers.Contains(ch)) markers++;
            else if (PolishLetters.Contains(ch) && ch != 'ó' && ch != 'Ó') polish++; // ó is identical in both
        }
        return markers >= 3 && polish == 0;
    }

    /// <summary>Reverses cp1250→cp1252 mojibake character by character (ASCII and unaffected text stay intact).</summary>
    public static string RepairPolishText(string input, bool collapseWhitespace = true)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder(input.Length);
        Span<byte> buf = stackalloc byte[4];
        foreach (var ch in input)
        {
            if (ch < 0x80) { sb.Append(ch); continue; }
            try
            {
                var n = Cp1252.GetBytes([ch], buf);
                sb.Append(n == 1 ? Cp1250.GetString(buf[..1]) : ch.ToString());
            }
            catch (EncoderFallbackException)
            {
                sb.Append(ch); // not representable in cp1252, so it was never mangled
            }
        }

        var result = sb.ToString().Normalize(NormalizationForm.FormC);
        if (collapseWhitespace) result = MultipleSpacesRegex().Replace(result, " ").Trim();
        return result;
    }

    public static string? NormalizeCell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var v = MultipleSpacesRegex().Replace(value.Replace(' ', ' '), " ").Trim();

        // Spreadsheet "force text" wrappers: '0012345' or ="0012345"
        if (v.Length >= 2 && v[0] == '\'' && v[^1] == '\'') v = v[1..^1].Trim();
        else if (v.Length >= 3 && v.StartsWith("=\"") && v[^1] == '"') v = v[2..^1].Trim();

        return v.Length == 0 ? null : v.Normalize(NormalizationForm.FormC);
    }

    /// <summary>Hash of the row plus how many identical rows preceded it, so genuine repeats are kept.</summary>
    public static string ComputeRowHash(IEnumerable<string?> values, int occurrence = 0)
    {
        var json = JsonSerializer.Serialize(values.ToArray());
        if (occurrence > 0) json += "#" + occurrence;
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(hashBytes);
    }

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy-M-d", "yyyy.MM.dd", "yyyy/MM/dd",
        "dd.MM.yyyy", "d.M.yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy",
    ];

    /// <summary>Returns the date part as yyyy-MM-dd; accepts an optional time ("2026-10-09 20:45", ISO "T").</summary>
    public static string? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var m = DateTimeRegex().Match(value.Trim());
        if (!m.Success) return null;

        return DateOnly.TryParseExact(m.Groups["date"].Value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// Parses an amount such as "1 234,56", "1.234,56", "1,234.56", "-12.30 PLN", "(5,00)".
    /// <paramref name="decimalSeparator"/> resolves ambiguous values like "1.234" for a whole column.
    /// </summary>
    public static decimal? TryParseAmount(string? value, char? decimalSeparator = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var v = WhitespaceRegex().Replace(value, "");
        v = CurrencyRegex().Replace(v, "");
        bool negative = false;
        if (v.StartsWith('(') && v.EndsWith(')')) { negative = true; v = v[1..^1]; }
        if (v.EndsWith('-')) { negative = true; v = v[..^1]; } // trailing minus (some ERPs)
        if (!AmountRegex().IsMatch(v)) return null;

        int lastDot = v.LastIndexOf('.'), lastComma = v.LastIndexOf(',');
        char? dec = decimalSeparator;
        if (lastDot >= 0 && lastComma >= 0) dec = lastDot > lastComma ? '.' : ',';
        else if (dec == null && (lastDot >= 0 || lastComma >= 0))
        {
            var sep = lastDot >= 0 ? '.' : ',';
            var parts = v.Split(sep);
            // "1.234" / "1,234,567" look like grouping; "12,5" / "3.14" like decimals.
            dec = parts.Length == 2 && parts[1].Length != 3 ? sep : parts.Length > 2 ? (sep == '.' ? ',' : '.') : sep;
        }

        char group = dec == '.' ? ',' : '.';
        v = v.Replace(group.ToString(), "");
        if (dec == ',') v = v.Replace(',', '.');

        if (!decimal.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result))
            return null;
        return negative ? -result : result;
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpacesRegex();

    [GeneratedRegex(@"[\s  ']")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(?:[A-Z]{3}|zł|ZŁ|PLN|[$€£])|(?:[A-Z]{3}|zł|ZŁ|[$€£])$")]
    private static partial Regex CurrencyRegex();

    [GeneratedRegex(@"^[+-]?(?:\d{1,3}(?:[.,]\d{3})+|\d+)(?:[.,]\d+)?$|^[+-]?[.,]\d+$")]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"^(?<date>\d{4}[-./]\d{1,2}[-./]\d{1,2}|\d{1,2}[-./]\d{1,2}[-./]\d{4})(?:[ T]\d{1,2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?$")]
    private static partial Regex DateTimeRegex();
}
