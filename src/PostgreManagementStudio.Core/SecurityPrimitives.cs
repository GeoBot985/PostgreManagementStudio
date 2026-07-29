using System.Collections;
using System.Text;
using System.Text.RegularExpressions;

namespace PostgreManagementStudio.Core;

public static partial class SensitiveDataRedactor
{
    public const string Replacement = "<redacted>";
    private static readonly string[] SensitiveKeys =
    [
        "password", "pwd", "passphrase", "token", "access token", "access_token",
        "client secret", "client_secret", "ssl password", "sslpassword", "private key",
        "pgpassword", "authorization", "proxy password",
    ];

    public static string Redact(string? value, IEnumerable<string?>? knownSecrets = null)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var result = ConnectionPropertyRegex().Replace(value, m => $"{m.Groups["key"].Value}={Replacement}");
        result = UriUserInfoRegex().Replace(result, "${scheme}${user}:" + Replacement + "@");
        result = BearerRegex().Replace(result, "${prefix}" + Replacement);
        if (knownSecrets is not null)
            foreach (var secret in knownSecrets.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal))
                result = result.Replace(secret!, Replacement, StringComparison.Ordinal);
        return result;
    }

    public static IReadOnlyDictionary<string, object?> RedactProperties(IEnumerable<KeyValuePair<string, object?>> properties)
        => properties.ToDictionary(x => x.Key, x => RedactValue(x.Key, x.Value), StringComparer.OrdinalIgnoreCase);

    public static SafeError ToSafeError(Exception exception, int maximumDepth = 4)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var messages = new List<string>();
        for (var current = exception; current is not null && messages.Count < Math.Clamp(maximumDepth, 1, 16); current = current.InnerException)
        {
            var message = Redact(current.Message);
            if (!string.IsNullOrWhiteSpace(message) && !messages.Contains(message, StringComparer.Ordinal))
                messages.Add(UntrustedText.ForDisplay(message, 2_048));
        }
        return new(exception.GetType().Name, messages.Count == 0 ? "The operation failed." : messages[0],
            messages.Skip(1).ToArray());
    }

    private static object? RedactValue(string key, object? value)
    {
        if (SensitiveKeys.Any(x => key.Contains(x, StringComparison.OrdinalIgnoreCase))) return Replacement;
        return value switch
        {
            null => null,
            string text => Redact(text),
            Exception error => ToSafeError(error),
            IReadOnlyDictionary<string, object?> dictionary => RedactProperties(dictionary),
            IDictionary dictionary => RedactProperties(dictionary.Cast<DictionaryEntry>()
                .Select(x => new KeyValuePair<string, object?>(Convert.ToString(x.Key) ?? "", x.Value))),
            IEnumerable enumerable when value is not string => enumerable.Cast<object?>()
                .Select(x => RedactValue("", x)).ToArray(),
            _ => value,
        };
    }

    [GeneratedRegex(@"(?ix)(?<key>password|pwd|passphrase|token|access[\s_]?token|client[\s_]?secret|ssl[\s_]?password|pgpassword|proxy[\s_]?password)\s*=\s*(?:""[^""]*""|'[^']*'|[^;\s,\]\}]+)")]
    private static partial Regex ConnectionPropertyRegex();

    [GeneratedRegex(@"(?i)(?<scheme>\b[a-z][a-z0-9+.-]*://)(?<user>[^:/@\s]+):[^@\s/]+@")]
    private static partial Regex UriUserInfoRegex();

    [GeneratedRegex(@"(?i)(?<prefix>\b(?:Bearer|Basic)\s+)[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BearerRegex();
}

public sealed record SafeError(string Type, string Message, IReadOnlyList<string> Causes);

public static class UntrustedText
{
    public const int DefaultDisplayLimit = 512;

    public static string ForDisplay(string? value, int maximumLength = DefaultDisplayLimit)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        maximumLength = Math.Clamp(maximumLength, 1, 65_536);
        var builder = new StringBuilder(Math.Min(value.Length, maximumLength));
        foreach (var rune in value.EnumerateRunes())
        {
            if (builder.Length >= maximumLength) break;
            if (Rune.IsControl(rune) || IsDirectionalOverride(rune.Value))
                builder.Append('\uFFFD');
            else
                builder.Append(rune);
        }
        if (value.Length > maximumLength) builder.Append('…');
        return builder.ToString();
    }

    public static string SafeFileName(string? databaseValue, string fallback = "export", int maximumLength = 120)
    {
        var value = ForDisplay(databaseValue, maximumLength).Trim();
        if (value is "." or ".." || string.IsNullOrWhiteSpace(value)) value = fallback;
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray()).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe[..Math.Min(safe.Length, maximumLength)];
    }

    private static bool IsDirectionalOverride(int value) => value is 0x202A or 0x202B or 0x202D or 0x202E or 0x202C
        or 0x2066 or 0x2067 or 0x2068 or 0x2069;
}
