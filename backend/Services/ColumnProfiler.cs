namespace CsvSerializer.Api.Services;

public class ColumnProfile
{
    public string Name { get; set; } = "";
    public bool IsDate { get; set; }
    public bool IsNumeric { get; set; }
    /// <summary>Decimal separator inferred for the whole column ('.' or ','), used for ambiguous values.</summary>
    public char? DecimalSeparator { get; set; }
    public int NonEmpty { get; set; }
}

/// <summary>Infers per-column types from all values, so one stray value cannot decide (or break) a column.</summary>
public static class ColumnProfiler
{
    private const double TypeThreshold = 0.95;

    public static List<ColumnProfile> Profile(List<string> headers, List<List<string?>> rows)
    {
        var result = new List<ColumnProfile>(headers.Count);
        for (int c = 0; c < headers.Count; c++)
        {
            var values = rows.Select(r => c < r.Count ? r[c] : null).Where(v => v != null).Cast<string>().ToList();
            var profile = new ColumnProfile { Name = headers[c], NonEmpty = values.Count };
            if (values.Count == 0) { result.Add(profile); continue; }

            profile.IsDate = Share(values, v => NormalizationService.TryParseDate(v) != null) >= TypeThreshold;

            if (!profile.IsDate && !values.Any(LooksLikeIdentifier))
            {
                profile.DecimalSeparator = InferDecimalSeparator(values);
                profile.IsNumeric = Share(values, v => NormalizationService.TryParseAmount(v, profile.DecimalSeparator) != null) >= TypeThreshold;
                if (!profile.IsNumeric) profile.DecimalSeparator = null;
            }

            result.Add(profile);
        }
        return result;
    }

    private static double Share(List<string> values, Func<string, bool> predicate) =>
        (double)values.Count(predicate) / values.Count;

    /// <summary>Account/card numbers, postcodes with leading zeros, long digit strings: text, not amounts.</summary>
    private static bool LooksLikeIdentifier(string v)
    {
        var digits = v.Count(char.IsDigit);
        if (digits >= 16 && !v.Contains(',') && !v.Contains('.')) return true;
        return v.Length > 1 && v[0] == '0' && char.IsDigit(v[1]) && v.All(char.IsDigit);
    }

    private static char? InferDecimalSeparator(List<string> values)
    {
        int dot = 0, comma = 0;
        foreach (var v in values)
        {
            int lastDot = v.LastIndexOf('.'), lastComma = v.LastIndexOf(',');
            if (lastDot >= 0 && lastComma >= 0) { if (lastDot > lastComma) dot += 10; else comma += 10; continue; }
            var sep = lastDot >= 0 ? '.' : lastComma >= 0 ? ',' : (char?)null;
            if (sep == null) continue;
            var parts = v.Split(sep.Value);
            if (parts.Length > 2) { if (sep == '.') comma += 10; else dot += 10; } // repeated = grouping
            else if (parts[1].Length != 3) { if (sep == '.') dot++; else comma++; }
        }
        return dot == 0 && comma == 0 ? null : dot >= comma ? '.' : ',';
    }
}
