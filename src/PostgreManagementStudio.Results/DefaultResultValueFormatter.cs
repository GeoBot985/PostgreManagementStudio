using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results;

public sealed class DefaultResultValueFormatter : IResultValueFormatter
{
    public string FormatForDisplay(ResultCell cell, ResultColumn column, ResultDisplayFormattingOptions options)
    {
        try
        {
            var text = cell.IsNull
                ? options.NullText
                : cell.Value is byte[] bytes
                    ? BinaryPreview(bytes, options.MaximumTextLength)
                    : cell.Value as string ?? FormatForSerialization(cell, column, new ResultSerializationFormattingOptions());
            if (options.UseSingleLinePreview)
                text = text.Replace("\r", options.ShowControlCharacterMarkers ? "↵" : " ")
                    .Replace("\n", options.ShowControlCharacterMarkers ? "↵" : " ")
                    .Replace("\t", options.ShowControlCharacterMarkers ? "→" : " ");
            return text.Length <= options.MaximumTextLength
                ? text
                : text[..Math.Max(0, options.MaximumTextLength - 1)] + "…";
        }
        catch (Exception ex)
        {
            return $"<formatting error: {ex.GetType().Name}>";
        }
    }

    public string FormatForSerialization(ResultCell cell, ResultColumn column, ResultSerializationFormattingOptions options)
    {
        if (cell.IsNull) return options.NullText;
        return FormatValue(cell.Value, options);
    }

    private static string FormatValue(object? value, ResultSerializationFormattingOptions options)
    {
        if (value is null) return options.NullText;
        if (value is byte[] bytes) return options.BinaryMode == BinarySerializationMode.Base64 ? Convert.ToBase64String(bytes) : "0x" + Convert.ToHexString(bytes);
        if (value is DateTime dt) return dt.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        if (value is DateTimeOffset dto) return dto.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);
        if (value is DateOnly date) return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (value is TimeOnly time) return time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        if (value is TimeSpan span) return span.ToString("c", CultureInfo.InvariantCulture);
        if (value is bool boolean) return boolean ? "true" : "false";
        if (value is float f) return float.IsNaN(f) ? "NaN" : float.IsPositiveInfinity(f) ? "Infinity" : float.IsNegativeInfinity(f) ? "-Infinity" : f.ToString("R", CultureInfo.InvariantCulture);
        if (value is double d) return double.IsNaN(d) ? "NaN" : double.IsPositiveInfinity(d) ? "Infinity" : double.IsNegativeInfinity(d) ? "-Infinity" : d.ToString("R", CultureInfo.InvariantCulture);
        if (value is decimal or BigInteger or sbyte or byte or short or ushort or int or uint or long or ulong) return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (value is JsonDocument json) return json.RootElement.GetRawText();
        if (value is IEnumerable enumerable and not string)
        {
            var items = new List<string>();
            foreach (var item in enumerable) items.Add(item is null ? options.NullText : FormatValue(item, options));
            return "{" + string.Join(",", items.Select(EscapeArrayItem)) + "}";
        }
        return value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty : value.ToString() ?? string.Empty;
    }

    private static string EscapeArrayItem(string item) => item.Contains(',') || item.Contains('"') || item.Contains('{') ? '"' + item.Replace("\"", "\\\"") + '"' : item;

    private static string BinaryPreview(byte[] bytes, int maximumTextLength)
    {
        var suffix = $"… ({bytes.Length:N0} bytes)";
        var previewBytes = Math.Min(bytes.Length, Math.Max(1, (maximumTextLength - 2 - suffix.Length) / 2));
        var preview = "0x" + Convert.ToHexString(bytes.AsSpan(0, previewBytes));
        return previewBytes == bytes.Length ? preview : preview + suffix;
    }
}
