using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CsvSerializer.Api.Services;

public static partial class NormalizationService
{
    private static readonly Dictionary<char, char> MojibakeMap = new()
    {
        { '\u0153', 'ś' }, // œ → ś
        { '\u0152', 'Ś' }, // Œ → Ś
        { '\u00A3', 'Ł' }, // £ → Ł
        { '\u00B3', 'ł' }, // ³ → ł
        { '\u00BF', 'ż' }, // ¿ → ż
        { '\u017D', 'Ż' }, // Ž → Ż (context-dependent)
        { '\u0178', 'ź' }, // Ÿ → ź
        { '\u00AF', 'Ź' }, // ¯ → Ź
        { '\u00E6', 'ć' }, // æ → ć
        { '\u00C6', 'Ć' }, // Æ → Ć
        { '\u00EA', 'ę' }, // ê → ę
        { '\u00CA', 'Ę' }, // Ê → Ę
        { '\u00B9', 'ą' }, // ¹ → ą
        { '\u00A5', 'Ą' }, // ¥ → Ą
        { '\u00F1', 'ń' }, // ñ → ń
        { '\u00D1', 'Ń' }, // Ñ → Ń
        { '\u00BC', 'ź' }, // ¼ → ź (alternate)
        { '\u00AA', 'ś' }, // ª → ś (alternate)
    };

    public static string RepairPolishText(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            sb.Append(MojibakeMap.TryGetValue(ch, out var replacement) ? replacement : ch);
        }

        var result = sb.ToString().Normalize(NormalizationForm.FormC);
        result = MultipleSpacesRegex().Replace(result, " ").Trim();
        return result;
    }

    public static string? NormalizeCell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        return RepairPolishText(trimmed);
    }

    public static string ComputeRowHash(IEnumerable<string?> values)
    {
        var json = JsonSerializer.Serialize(values.ToArray());
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(hashBytes);
    }

    public static string? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var v = value.Trim();

        if (Regex.IsMatch(v, @"^\d{4}-\d{2}-\d{2}$"))
            return v;

        var match = Regex.Match(v, @"^(\d{2})[.\-/](\d{2})[.\-/](\d{4})$");
        if (match.Success)
            return $"{match.Groups[3].Value}-{match.Groups[2].Value}-{match.Groups[1].Value}";

        return null;
    }

    public static decimal? TryParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var v = value.Trim().Replace(" ", "");
        v = Regex.Replace(v, @"\.(?=\d{3}(?:\D|$))", "");
        v = v.Replace(",", ".");

        if (decimal.TryParse(v, CultureInfo.InvariantCulture, out var result))
            return Math.Round(result, 2);

        return null;
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpacesRegex();
}
