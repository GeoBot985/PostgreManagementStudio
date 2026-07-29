using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results;

public sealed class DefaultResultValueFormatter : IResultValueFormatter
{
    public string FormatForDisplay(ResultCell cell, ResultColumn column, ResultDisplayFormattingOptions options)
    {
        try
        {
            var text = cell.IsNull ? options.NullText : FormatDisplayValue(cell.Value, options.MaximumTextLength);
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

    private static string FormatDisplayValue(object? value, int maximumTextLength)
    {
        if (value is null) return string.Empty;
        if (value is byte[] bytes) return BinaryPreview(bytes, maximumTextLength);
        if (value is string text) return Bounded(text, maximumTextLength);
        if (value is JsonDocument document)
            return JsonPreview(document.RootElement, maximumTextLength);
        if (value is JsonElement element)
            return JsonPreview(element, maximumTextLength);
        if (value is IEnumerable enumerable)
            return EnumerablePreview(enumerable, maximumTextLength);
        return Bounded(FormatValue(value, new()), maximumTextLength);
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

    private static string EnumerablePreview(IEnumerable values, int maximumTextLength)
    {
        var builder = new StringBuilder(Math.Min(maximumTextLength, 256));
        builder.Append('{');
        var count = 0;
        foreach (var value in values)
        {
            if (count++ > 0) builder.Append(',');
            var item = value is null ? "NULL" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            var remaining = maximumTextLength - builder.Length - 2;
            if (remaining <= 0)
            {
                builder.Append('…');
                break;
            }
            if (item.Length > remaining)
            {
                builder.Append(item.AsSpan(0, Math.Max(0, remaining - 1)));
                builder.Append('…');
                break;
            }
            builder.Append(item);
            if (count >= 32)
            {
                builder.Append(",…");
                break;
            }
        }
        builder.Append('}');
        return Bounded(builder.ToString(), maximumTextLength);
    }

    private static string JsonPreview(JsonElement element, int maximumTextLength)
    {
        var builder = new StringBuilder(Math.Min(maximumTextLength, 256));
        AppendJson(element, builder, maximumTextLength, 0);
        return Bounded(builder.ToString(), maximumTextLength);
    }

    private static void AppendJson(JsonElement element, StringBuilder builder, int limit, int depth)
    {
        if (builder.Length >= limit - 1)
        {
            builder.Append('…');
            return;
        }
        if (depth >= 8)
        {
            builder.Append("…");
            return;
        }
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var propertyCount = 0;
                foreach (var property in element.EnumerateObject())
                {
                    if (propertyCount++ > 0) builder.Append(',');
                    AppendBounded(builder, JsonSerializer.Serialize(property.Name), limit);
                    builder.Append(':');
                    AppendJson(property.Value, builder, limit, depth + 1);
                    if (builder.Length >= limit - 1 || propertyCount >= 32) { builder.Append('…'); break; }
                }
                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var itemCount = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (itemCount++ > 0) builder.Append(',');
                    AppendJson(item, builder, limit, depth + 1);
                    if (builder.Length >= limit - 1 || itemCount >= 32) { builder.Append('…'); break; }
                }
                builder.Append(']');
                break;
            case JsonValueKind.String:
                AppendBounded(builder, JsonSerializer.Serialize(element.GetString()), limit);
                break;
            default:
                AppendBounded(builder, element.ToString(), limit);
                break;
        }
    }

    private static void AppendBounded(StringBuilder builder, string value, int limit)
    {
        var remaining = Math.Max(0, limit - builder.Length);
        if (value.Length <= remaining) builder.Append(value);
        else if (remaining > 0)
        {
            builder.Append(value.AsSpan(0, Math.Max(0, remaining - 1)));
            builder.Append('…');
        }
    }

    private static string Bounded(string value, int maximumTextLength) =>
        value.Length <= maximumTextLength
            ? value
            : value[..Math.Max(0, maximumTextLength - 1)] + "…";
}
