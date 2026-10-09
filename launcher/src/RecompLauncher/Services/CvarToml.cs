using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RecompLauncher.Services;

/// <summary>
/// Minimal reader/writer for the flat cvar TOML files the ReXGlue runtime
/// loads next to the game exe (e.g. puzzlefighter.toml). Round-trips unknown
/// keys, comments, and ordering verbatim; only assignment lines for known
/// keys are rewritten in place, missing keys are appended.
/// </summary>
public static partial class CvarToml
{
    [GeneratedRegex(@"^\s*([A-Za-z0-9_.\-]+)\s*=\s*(.*?)\s*$")]
    private static partial Regex AssignmentRegex();

    /// <summary>Parses the file into an ordered key -> string map. Missing file yields an empty map.</summary>
    public static Dictionary<string, string> Read(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return result;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = StripComment(rawLine).TrimEnd();
            var match = AssignmentRegex().Match(line);
            if (!match.Success)
                continue;

            var key = match.Groups[1].Value;
            var value = ParseValue(match.Groups[2].Value.Trim());
            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Writes the given values into the TOML file. Existing assignment lines
    /// for these keys are replaced in place; new keys are appended. All other
    /// lines (unknown keys, comments, blank lines) are preserved verbatim.
    /// </summary>
    public static void Write(string path, IReadOnlyDictionary<string, string> values)
    {
        var lines = File.Exists(path)
            ? new List<string>(File.ReadAllLines(path))
            : new List<string> { "# RecompLauncher managed cvar configuration." };

        var pending = new Dictionary<string, string>(values, StringComparer.Ordinal);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = StripComment(lines[i]).TrimEnd();
            var match = AssignmentRegex().Match(line);
            if (!match.Success)
                continue;

            var key = match.Groups[1].Value;
            if (pending.TryGetValue(key, out var newValue))
            {
                var indent = match.Groups[1].Value.Length > 0 && lines[i].StartsWith(' ')
                    ? new string(' ', lines[i].IndexOf(match.Groups[1].Value[0]))
                    : "";
                lines[i] = $"{indent}{key} = {FormatValue(newValue)}";
                pending.Remove(key);
            }
        }

        if (pending.Count > 0)
        {
            // Never append top-level keys under somebody else's [section].
            var hasSection = lines.Any(l => Regex.IsMatch(l, @"^\s*\["));
            if (hasSection)
                lines.Add("");

            foreach (var (key, value) in pending)
                lines.Add($"{key} = {FormatValue(value)}");
        }

        File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
    }

    /// <summary>Removes the assignment lines for the given keys (factory reset).</summary>
    public static void Remove(string path, IEnumerable<string> keys)
    {
        if (!File.Exists(path))
            return;

        var removeSet = new HashSet<string>(keys, StringComparer.Ordinal);
        var lines = File.ReadAllLines(path)
            .Where(line =>
            {
                var match = AssignmentRegex().Match(StripComment(line).TrimEnd());
                return !(match.Success && removeSet.Contains(match.Groups[1].Value));
            })
            .ToList();

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    /// <summary>Removes a trailing # comment that is not inside a quoted string.</summary>
    private static string StripComment(string line)
    {
        var inQuote = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && (i == 0 || line[i - 1] != '\\'))
                inQuote = !inQuote;
            else if (c == '#' && !inQuote)
                return line[..i];
        }

        return line;
    }

    /// <summary>Converts a raw TOML value into the plain string the UI works with.</summary>
    private static string ParseValue(string raw)
    {
        if (raw.Length >= 2 && raw.StartsWith('"') && raw.EndsWith('"'))
        {
            var inner = raw[1..^1];
            return inner.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        return raw;
    }

    /// <summary>Formats a plain UI string back into TOML, quoting only when necessary.</summary>
    private static string FormatValue(string value)
    {
        if (bool.TryParse(value, out var b))
            return b ? "true" : "false";

        if (long.TryParse(value, out _))
            return value;

        if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            return value;

        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }
}
