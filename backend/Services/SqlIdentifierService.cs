using System.Text;
using System.Text.RegularExpressions;

namespace CsvSerializer.Api.Services;

public static partial class SqlIdentifierService
{
    private static readonly Dictionary<char, string> PolishTranslit = new()
    {
        { 'ą', "a" }, { 'ć', "c" }, { 'ę', "e" }, { 'ł', "l" }, { 'ń', "n" },
        { 'ó', "o" }, { 'ś', "s" }, { 'ź', "z" }, { 'ż', "z" },
        { 'Ą', "a" }, { 'Ć', "c" }, { 'Ę', "e" }, { 'Ł', "l" }, { 'Ń', "n" },
        { 'Ó', "o" }, { 'Ś', "s" }, { 'Ź', "z" }, { 'Ż', "z" },
    };

    public static string NormalizeIdentifier(string raw)
    {
        var sb = new StringBuilder();
        foreach (var ch in raw.ToLowerInvariant())
        {
            if (PolishTranslit.TryGetValue(ch, out var repl))
                sb.Append(repl);
            else if (char.IsLetterOrDigit(ch) || ch == '_')
                sb.Append(ch);
            else
                sb.Append('_');
        }

        var result = sb.ToString();
        result = MultiUnderscoreRegex().Replace(result, "_");
        result = result.Trim('_');

        if (string.IsNullOrEmpty(result))
            result = "col";

        if (char.IsDigit(result[0]))
            result = "_" + result;

        return result;
    }

    public static List<string> NormalizeHeaders(IEnumerable<string> rawHeaders)
    {
        var result = new List<string>();
        var seen = new Dictionary<string, int>();

        foreach (var raw in rawHeaders)
        {
            var clean = raw.TrimStart('#').Trim();
            var ident = NormalizeIdentifier(clean);

            if (seen.TryGetValue(ident, out var count))
            {
                seen[ident] = count + 1;
                ident = $"{ident}_{count + 1}";
            }
            else
            {
                seen[ident] = 1;
            }

            result.Add(ident);
        }

        return result;
    }

    public static string NormalizeTableName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Table name is required.");

        var name = NormalizeIdentifier(raw.Trim());

        if (name.Length < 3)
            throw new ArgumentException("Table name must be at least 3 characters.");

        if (name.Length > 64)
            name = name[..64];

        if (!char.IsLetter(name[0]) && name[0] != '_')
            throw new ArgumentException("Table name must start with a letter or underscore.");

        return name;
    }

    [GeneratedRegex(@"_{2,}")]
    private static partial Regex MultiUnderscoreRegex();
}
