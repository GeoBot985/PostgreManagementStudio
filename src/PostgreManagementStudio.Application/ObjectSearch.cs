using System.Text.RegularExpressions;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public enum SearchObjectType { Table, View, MaterializedView, Sequence, Index, Function, Procedure, Column, Trigger }
public sealed record ObjectSearchOptions(string Text, IReadOnlySet<SearchObjectType>? ObjectTypes = null, bool SearchDefinitions = false, bool IncludeSystemObjects = false, int MaximumResults = 1000)
{ public string NormalizedText => Text.Trim(); }
public sealed record ObjectSearchResult(
    SearchObjectType ObjectType,
    string Database,
    string Schema,
    string ObjectName,
    string? ParentObject,
    string MatchType,
    string? MatchPreview,
    PostgresObjectIdentity? Identity = null);
public sealed record ObjectSearchQuery(string Sql, IReadOnlyDictionary<string, object?> Parameters);
public sealed record ObjectSearchBatch(IReadOnlyList<ObjectSearchResult> Results, IReadOnlyList<string> Warnings, bool LimitReached, TimeSpan Duration);
public sealed record ObjectSearchHistoryEntry(string Text, string? DatabaseScope, IReadOnlySet<SearchObjectType>? ObjectTypes, bool SearchDefinitions, DateTimeOffset Timestamp);
public static class ObjectSearchQueryBuilder
{
    public static ObjectSearchQuery Build(ObjectSearchOptions options)
    { if (string.IsNullOrWhiteSpace(options.NormalizedText)) throw new ArgumentException("Search text is required."); if (options.MaximumResults <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaximumResults)); var conditions = new List<string> { "(n.nspname NOT LIKE 'pg_%' AND n.nspname <> 'information_schema')", "(c.relname ILIKE @pattern ESCAPE '\\' OR n.nspname || '.' || c.relname ILIKE @pattern ESCAPE '\\')" }; if (options.IncludeSystemObjects) conditions[0] = "TRUE"; var typeCondition = options.ObjectTypes is { Count: > 0 } ? "AND c.relkind = ANY(@relkinds)" : ""; var definition = options.SearchDefinitions ? ", pg_get_viewdef(c.oid, true)" : ", NULL::text"; var sql = $"SELECT CASE c.relkind WHEN 'r' THEN 'Table' WHEN 'p' THEN 'Table' WHEN 'f' THEN 'Table' WHEN 'v' THEN 'View' WHEN 'm' THEN 'MaterializedView' WHEN 'S' THEN 'Sequence' WHEN 'i' THEN 'Index' ELSE 'Table' END, current_database(), n.nspname, c.relname, NULL::text, CASE WHEN c.relname ILIKE @pattern ESCAPE '\\' THEN 'Name' ELSE 'Name' END{definition}, c.oid::bigint, n.oid::bigint, (SELECT oid::bigint FROM pg_database WHERE datname=current_database()), COALESCE(inet_server_addr()::text, 'local') || ':' || COALESCE(inet_server_port()::text, current_setting('port')) || ':' || current_setting('server_version_num'), c.relkind, c.relispartition FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE {string.Join(" AND ", conditions)} {typeCondition} ORDER BY n.nspname,c.relname,c.oid LIMIT @limit"; var kinds = options.ObjectTypes?.Select(x => x switch { SearchObjectType.Table => "r", SearchObjectType.View => "v", SearchObjectType.MaterializedView => "m", SearchObjectType.Sequence => "S", SearchObjectType.Index => "i", _ => "?" }).Where(x => x != "?").ToArray(); return new(sql, new Dictionary<string, object?> { ["pattern"] = ToLikePattern(options.NormalizedText), ["limit"] = options.MaximumResults, ["relkinds"] = kinds ?? Array.Empty<string>() }); }
    public static string ToLikePattern(string text) { var escaped = Regex.Replace(text, @"([\\%_])", "\\$1"); return "%" + escaped.Replace("*", "%") + "%"; }
}
public sealed class ObjectSearchHistoryService(int maximumEntries = 20)
{ private readonly LinkedList<ObjectSearchHistoryEntry> _entries = new(); public IReadOnlyList<ObjectSearchHistoryEntry> Entries => _entries.ToArray(); public void Add(ObjectSearchHistoryEntry entry) { var old = _entries.First; while (old is not null) { var next = old.Next; if (old.Value.Text == entry.Text && old.Value.DatabaseScope == entry.DatabaseScope) _entries.Remove(old); old = next; } _entries.AddFirst(entry); while (_entries.Count > Math.Max(1, maximumEntries)) _entries.RemoveLast(); } public void Remove(ObjectSearchHistoryEntry entry) => _entries.Remove(entry); public void Clear() => _entries.Clear(); }
public static class ObjectSearchResultUtilities
{ public static IReadOnlyList<ObjectSearchResult> Deduplicate(IEnumerable<ObjectSearchResult> results) => results.GroupBy(x => (x.Database, x.Schema, x.ObjectName, x.ObjectType)).Select(x => x.First()).ToArray(); }
public interface IObjectNavigationService { Task<bool> NavigateAsync(ObjectSearchResult result, CancellationToken cancellationToken = default); }
