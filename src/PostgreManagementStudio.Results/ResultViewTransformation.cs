using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Results;

public enum SortDirection { Ascending, Descending }
public enum NullPlacement { First, Last }
public enum LogicalOperator { And, Or }
public enum FilterOperator { IsNull, IsNotNull, IsEmpty, IsNotEmpty, Equals, NotEquals, Contains, NotContains, StartsWith, EndsWith, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Between, Regex }

public sealed record SortDescriptor(int ColumnOrdinal, SortDirection Direction = SortDirection.Ascending, NullPlacement NullPlacement = NullPlacement.Last, int Priority = 0);
public abstract record FilterExpression;
public sealed record FilterCondition(int ColumnOrdinal, FilterOperator Operator, object? FirstValue = null, object? SecondValue = null, bool CaseSensitive = false) : FilterExpression;
public sealed record FilterGroup(LogicalOperator Operator, IReadOnlyList<FilterExpression> Children) : FilterExpression;
public sealed record SearchState(string Text = "", bool CaseSensitive = false, bool Regex = false, int CurrentMatch = -1) { public bool Active => !string.IsNullOrEmpty(Text); }
public sealed record ResultViewState(IReadOnlyList<SortDescriptor> Sorts, FilterExpression? Filter, SearchState Search)
{
    public static ResultViewState Empty { get; } = new(Array.Empty<SortDescriptor>(), null, new());
}
public sealed record ResultViewResult(IReadOnlyList<int> VisibleRowIndexes, IReadOnlyList<int> SearchMatches, string? Error = null);

public sealed class ResultViewTransformationService
{
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
    private static readonly ConcurrentQueue<string> RegexInsertionOrder = new();
    internal const int MaximumRegexCacheEntries = 64;
    internal static int CachedRegexCount => RegexCache.Count;
    public Task<ResultViewResult> TransformAsync(ResultSetSchema schema, IReadOnlyList<ResultRow> rows, ResultViewState state, CancellationToken cancellationToken = default)
        => Task.Run(() => Transform(schema, rows, state, cancellationToken), cancellationToken);

    public ResultViewResult Transform(ResultSetSchema schema, IReadOnlyList<ResultRow> rows, ResultViewState state, CancellationToken cancellationToken = default)
    {
        try
        {
            var indexes = Enumerable.Range(0, rows.Count).Where(i => MatchesFilter(rows[i], state.Filter, cancellationToken)).Where(i => MatchesSearch(rows[i], schema, state.Search, cancellationToken)).ToList();
            if (state.Sorts.Count > 0)
            {
                var sorts = state.Sorts.Select((x, order) => (x, order)).OrderBy(x => x.x.Priority).ThenBy(x => x.order).Select(x => x.x).ToArray();
                indexes.Sort((left, right) => { foreach (var sort in sorts) { var result = new CellComparer(sort).Compare(rows[left].Cells.ElementAtOrDefault(sort.ColumnOrdinal), rows[right].Cells.ElementAtOrDefault(sort.ColumnOrdinal)); if (result != 0) return result; } return left.CompareTo(right); });
            }
            var matches = state.Search.Active ? indexes.Where(i => MatchesSearch(rows[i], schema, state.Search with { Text = state.Search.Text }, cancellationToken)).ToArray() : Array.Empty<int>();
            return new(indexes, matches);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException or RegexParseException)
        { return new(Array.Empty<int>(), Array.Empty<int>(), ex.Message); }
    }

    private static bool MatchesFilter(ResultRow row, FilterExpression? expression, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (expression is null) return true;
        if (expression is FilterGroup group) return group.Operator == LogicalOperator.And ? group.Children.All(x => MatchesFilter(row, x, token)) : group.Children.Any(x => MatchesFilter(row, x, token));
        var f = (FilterCondition)expression; var cell = row.Cells.ElementAtOrDefault(f.ColumnOrdinal); var value = cell?.Value; var text = value?.ToString() ?? ""; var comparison = StringComparisonFrom(f.CaseSensitive);
        return f.Operator switch
        {
            FilterOperator.IsNull => cell is null || cell.IsNull || value is null,
            FilterOperator.IsNotNull => cell is not null && !cell.IsNull && value is not null,
            FilterOperator.IsEmpty => cell is not null && !cell.IsNull && text.Length == 0,
            FilterOperator.IsNotEmpty => cell is not null && !cell.IsNull && text.Length > 0,
            FilterOperator.Contains => text.Contains(Convert.ToString(f.FirstValue, CultureInfo.InvariantCulture) ?? "", comparison),
            FilterOperator.NotContains => !text.Contains(Convert.ToString(f.FirstValue, CultureInfo.InvariantCulture) ?? "", comparison),
            FilterOperator.StartsWith => text.StartsWith(Convert.ToString(f.FirstValue, CultureInfo.InvariantCulture) ?? "", comparison),
            FilterOperator.EndsWith => text.EndsWith(Convert.ToString(f.FirstValue, CultureInfo.InvariantCulture) ?? "", comparison),
            FilterOperator.Regex => GetRegex(
                Convert.ToString(f.FirstValue) ?? string.Empty,
                f.CaseSensitive).IsMatch(text),
            FilterOperator.Equals or FilterOperator.NotEquals or FilterOperator.GreaterThan or FilterOperator.GreaterThanOrEqual or FilterOperator.LessThan or FilterOperator.LessThanOrEqual or FilterOperator.Between => CompareFilter(value, f),
            _ => false
        };
    }
    private static StringComparison StringComparisonFrom(bool sensitive) => sensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    private static bool CompareFilter(object? value, FilterCondition f)
    { if (value is null) return false; var c = CompareValues(value, f.FirstValue); return f.Operator switch { FilterOperator.Equals => c == 0, FilterOperator.NotEquals => c != 0, FilterOperator.GreaterThan => c > 0, FilterOperator.GreaterThanOrEqual => c >= 0, FilterOperator.LessThan => c < 0, FilterOperator.LessThanOrEqual => c <= 0, FilterOperator.Between => c >= 0 && CompareValues(value, f.SecondValue) <= 0, _ => false }; }
    internal static int CompareValues(object? a, object? b)
    { if (a is null && b is null) return 0; if (a is null) return -1; if (b is null) return 1; if (a is IConvertible && b is IConvertible && decimal.TryParse(Convert.ToString(a, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var da) && decimal.TryParse(Convert.ToString(b, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var db)) return da.CompareTo(db); if (a is IComparable ca) try { return ca.CompareTo(b); } catch { } return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase); }
    private static bool MatchesSearch(ResultRow row, ResultSetSchema schema, SearchState search, CancellationToken token) { if (!search.Active) return true; Regex? regex = search.Regex ? GetRegex(search.Text, search.CaseSensitive) : null; foreach (var cell in row.Cells) { token.ThrowIfCancellationRequested(); var text = cell.IsNull ? "NULL" : cell.Value?.ToString() ?? ""; if (regex?.IsMatch(text) == true || (!search.Regex && text.Contains(search.Text, StringComparisonFrom(search.CaseSensitive)))) return true; } return false; }
    private static Regex GetRegex(string pattern, bool caseSensitive)
    {
        var key = $"{caseSensitive}:{pattern}";
        if (RegexCache.TryGetValue(key, out var cached)) return cached;
        var options = caseSensitive ? RegexOptions.CultureInvariant : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var created = new Regex(pattern, options, TimeSpan.FromMilliseconds(250));
        var stored = RegexCache.GetOrAdd(key, created);
        if (ReferenceEquals(stored, created))
        {
            RegexInsertionOrder.Enqueue(key);
            while (RegexCache.Count > MaximumRegexCacheEntries && RegexInsertionOrder.TryDequeue(out var oldest))
                RegexCache.TryRemove(oldest, out _);
        }
        return stored;
    }
    private sealed class CellComparer(SortDescriptor sort) : IComparer<ResultCell?>
    { public int Compare(ResultCell? x, ResultCell? y) { var xn = x is null || x.IsNull || x.Value is null; var yn = y is null || y.IsNull || y.Value is null; if (xn || yn) { if (xn && yn) return 0; return xn == (sort.NullPlacement == NullPlacement.First) ? -1 : 1; } var c = CompareValues(x!.Value, y!.Value); return sort.Direction == SortDirection.Descending ? -c : c; } }
}
