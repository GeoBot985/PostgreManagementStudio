using System.Text;
using System.Text.RegularExpressions;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public sealed record EditorObjectReference(
    string DisplayText,
    IReadOnlyList<string> NameParts,
    string? RelationAlias = null,
    string? MemberName = null,
    string? RoutineSignature = null,
    bool IsEditorLocal = false);

public sealed record DescriptionEditorBinding(
    Guid QueryTabId,
    long DocumentVersion,
    int CaretIndex,
    string SqlSnapshot,
    Guid ConnectionGenerationId,
    string Database);

public interface IEditorObjectResolver
{
    EditorObjectReference? Resolve(string sql, int caretIndex, int selectionStart, int selectionLength);
}

public sealed class EditorObjectResolver : IEditorObjectResolver
{
    private static readonly Regex RelationAliasPattern = new(
        """(?ix)\b(?:from|join|update|into|using)\s+(?<name>(?:"(?:[^"]|"")*"|[\p{L}_][\p{L}\p{N}_$]*)(?:\s*\.\s*(?:"(?:[^"]|"")*"|[\p{L}_][\p{L}\p{N}_$]*))?)(?:\s+(?:as\s+)?(?<alias>"(?:[^"]|"")*"|[\p{L}_][\p{L}\p{N}_$]*))?""",
        RegexOptions.Compiled);
    private static readonly Regex CtePattern = new(
        """(?ix)(?:\bwith\b|,)\s*(?<name>"(?:[^"]|"")*"|[\p{L}_][\p{L}\p{N}_$]*)\s*(?:\([^)]*\))?\s+as\s*\(""",
        RegexOptions.Compiled);

    public EditorObjectReference? Resolve(
        string sql, int caretIndex, int selectionStart, int selectionLength)
    {
        sql ??= string.Empty;
        var selected = selectionLength > 0
            ? sql.Substring(Math.Clamp(selectionStart, 0, sql.Length),
                Math.Min(selectionLength, sql.Length - Math.Clamp(selectionStart, 0, sql.Length))).Trim()
            : null;
        var text = !string.IsNullOrWhiteSpace(selected)
            ? selected!
            : ExtractIdentifier(sql, Math.Clamp(caretIndex, 0, sql.Length));
        if (string.IsNullOrWhiteSpace(text)) return null;

        var (identifier, signature) = SplitRoutineSignature(text);
        var parts = ParseParts(identifier);
        if (parts.Count == 0) return null;

        var statement = CurrentStatement(sql, Math.Clamp(caretIndex, 0, sql.Length));
        var ctes = CtePattern.Matches(statement)
            .Select(match => Unquote(match.Groups["name"].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var aliases = ReadAliases(statement);
        if (parts.Count is 1 or 2 && aliases.TryGetValue(parts[0], out var aliasBinding))
        {
            var member = parts.Count == 2 ? parts[1] : null;
            return new(text, aliasBinding.Relation,
                aliasBinding.IsExplicit ? parts[0] : null, member, signature,
                IsEditorLocal: ctes.Contains(aliasBinding.Relation[0]));
        }
        var matchingAlias = aliases.FirstOrDefault(pair =>
            pair.Value.Relation.SequenceEqual(parts, StringComparer.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(matchingAlias.Key))
            return new(text, parts,
                matchingAlias.Value.IsExplicit ? matchingAlias.Key : null,
                RoutineSignature: signature,
                IsEditorLocal: ctes.Contains(parts[0]));

        if (ctes.Contains(parts[0]))
            return new(text, parts, parts.Count > 1 ? parts[0] : null,
                parts.Count > 1 ? parts[^1] : null, signature, IsEditorLocal: true);

        return new(text, parts, RoutineSignature: signature);
    }

    private static string ExtractIdentifier(string sql, int caret)
    {
        if (sql.Length == 0) return string.Empty;
        var tokens = IdentifierTokens(sql);
        var tokenIndex = tokens.FindIndex(token =>
            token.Text is not "." and not "("
            && (caret >= token.Start && caret < token.End
                || caret > 0 && caret - 1 >= token.Start && caret - 1 < token.End));
        if (tokenIndex < 0) return string.Empty;
        var first = tokenIndex;
        var last = tokenIndex;
        while (first >= 2 && IsDotBetween(sql, tokens[first - 2], tokens[first - 1], tokens[first]))
            first -= 2;
        while (last + 2 < tokens.Count && IsDotBetween(sql, tokens[last], tokens[last + 1], tokens[last + 2]))
            last += 2;
        var start = tokens[first].Start;
        var end = tokens[last].End;
        if (last + 1 < tokens.Count && tokens[last + 1].Text == "(")
        {
            var close = FindBalancedClose(sql, tokens[last + 1].Start);
            if (close >= 0) end = close + 1;
        }
        return sql[start..end].Trim();
    }

    private static bool IsDotBetween(string sql, Token left, Token dot, Token right) =>
        dot.Text == "." && string.IsNullOrWhiteSpace(sql[left.End..dot.Start])
        && string.IsNullOrWhiteSpace(sql[dot.End..right.Start]);

    private static List<Token> IdentifierTokens(string sql)
    {
        var result = new List<Token>();
        for (var i = 0; i < sql.Length;)
        {
            if (sql[i] == '"')
            {
                var start = i++;
                while (i < sql.Length)
                {
                    if (sql[i++] != '"') continue;
                    if (i < sql.Length && sql[i] == '"') { i++; continue; }
                    break;
                }
                result.Add(new(start, i, sql[start..i]));
            }
            else if (IsIdentifierStart(sql[i]))
            {
                var start = i++;
                while (i < sql.Length && IsIdentifierPart(sql[i])) i++;
                result.Add(new(start, i, sql[start..i]));
            }
            else if (sql[i] is '.' or '(')
            {
                result.Add(new(i, i + 1, sql[i].ToString()));
                i++;
            }
            else i++;
        }
        return result;
    }

    private static int FindBalancedClose(string sql, int open)
    {
        var depth = 0;
        var quoted = false;
        for (var i = open; i < sql.Length; i++)
        {
            if (sql[i] == '"' && (i + 1 >= sql.Length || sql[i + 1] != '"')) quoted = !quoted;
            if (quoted) continue;
            if (sql[i] == '(') depth++;
            else if (sql[i] == ')' && --depth == 0) return i;
            if (sql[i] is ';' or '\n' && depth == 0) break;
        }
        return -1;
    }

    private static (string Identifier, string? Signature) SplitRoutineSignature(string value)
    {
        var open = value.IndexOf('(');
        if (open < 0 || !value.EndsWith(')')) return (value, null);
        return (value[..open].Trim(), value[(open + 1)..^1].Trim());
    }

    internal static IReadOnlyList<string> ParseParts(string value)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character == '"')
            {
                if (quoted && i + 1 < value.Length && value[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else quoted = !quoted;
            }
            else if (character == '.' && !quoted)
            {
                if (current.Length > 0) parts.Add(current.ToString());
                current.Clear();
            }
            else if (!char.IsWhiteSpace(character) || quoted) current.Append(character);
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    private static Dictionary<string, RelationBinding> ReadAliases(string statement)
    {
        var result = new Dictionary<string, RelationBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in RelationAliasPattern.Matches(statement))
        {
            var parts = ParseParts(match.Groups["name"].Value);
            if (parts.Count == 0) continue;
            var explicitAlias = match.Groups["alias"].Success
                && !IsClauseKeyword(Unquote(match.Groups["alias"].Value));
            var alias = explicitAlias
                ? Unquote(match.Groups["alias"].Value)
                : parts[^1];
            result[alias] = new(parts, explicitAlias);
        }
        return result;
    }

    private static string CurrentStatement(string sql, int caret)
    {
        var start = sql.LastIndexOf(';', Math.Max(0, caret - 1));
        var end = sql.IndexOf(';', caret);
        return sql[(start < 0 ? 0 : start + 1)..(end < 0 ? sql.Length : end)];
    }

    private static bool IsClauseKeyword(string value) =>
        value.Equals("where", StringComparison.OrdinalIgnoreCase)
        || value.Equals("set", StringComparison.OrdinalIgnoreCase)
        || value.Equals("join", StringComparison.OrdinalIgnoreCase)
        || value.Equals("on", StringComparison.OrdinalIgnoreCase)
        || value.Equals("returning", StringComparison.OrdinalIgnoreCase)
        || value.Equals("group", StringComparison.OrdinalIgnoreCase)
        || value.Equals("order", StringComparison.OrdinalIgnoreCase)
        || value.Equals("limit", StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)
            : value;
    private static bool IsIdentifierStart(char value) => value == '_' || char.IsLetter(value);
    private static bool IsIdentifierPart(char value) => value is '_' or '$' || char.IsLetterOrDigit(value);
    private sealed record Token(int Start, int End, string Text);
    private sealed record RelationBinding(IReadOnlyList<string> Relation, bool IsExplicit);
}

public sealed record ObjectDescriptionCandidate(
    PostgresObjectIdentity Identity,
    string QualifiedName,
    string ObjectType,
    string Owner,
    string? Signature,
    bool IsTemporary,
    bool IsVisible);

public sealed record ObjectDescriptionColumn(
    int Ordinal,
    string Name,
    string DataType,
    bool IsNullable,
    string? DefaultExpression,
    string IdentityMode,
    string? GeneratedExpression,
    string? Collation,
    bool IsPrimaryKey,
    bool IsUnique,
    bool IsForeignKey,
    string? ForeignKeyReference,
    string? Comment,
    IReadOnlyList<string>? Indexes = null,
    string? Privileges = null)
{
    public bool IsWritable => GeneratedExpression is null && IdentityMode != "ALWAYS";
    public bool IsRequiredInsert => IsWritable && !IsNullable && DefaultExpression is null;
    public bool IsObviouslyLarge => DataType.Equals("bytea", StringComparison.OrdinalIgnoreCase)
        || DataType.Equals("text", StringComparison.OrdinalIgnoreCase);
}

public sealed record ObjectDescription(
    ObjectDescriptionCandidate Candidate,
    string Persistence,
    string? Comment,
    string? Tablespace,
    string? RelationStatus,
    long? EstimatedRows,
    long? SizeBytes,
    IReadOnlyList<ObjectDescriptionColumn> Columns,
    string DetailsText,
    string? Definition,
    string? TargetColumn = null);

public sealed record ObjectDescriptionSecondaryDetails(long? SizeBytes, string DetailsText);

public interface IObjectDescriptionMetadataProvider
{
    Task<IReadOnlyList<ObjectDescriptionCandidate>> ResolveAsync(
        string connectionString, string database, EditorObjectReference reference,
        CancellationToken cancellationToken = default);
    Task<ObjectDescription> LoadAsync(
        string connectionString, string database, ObjectDescriptionCandidate candidate,
        string? targetColumn, CancellationToken cancellationToken = default);
    Task<ObjectDescriptionSecondaryDetails> LoadSecondaryAsync(
        string connectionString, string database, ObjectDescriptionCandidate candidate,
        CancellationToken cancellationToken = default);
}

public sealed class ObjectDescriptionService(IObjectDescriptionMetadataProvider provider)
{
    public Task<IReadOnlyList<ObjectDescriptionCandidate>> ResolveAsync(
        string connectionString, string database, EditorObjectReference reference,
        CancellationToken cancellationToken = default) =>
        provider.ResolveAsync(connectionString, database, reference, cancellationToken);

    public Task<ObjectDescription> LoadAsync(
        string connectionString, string database, ObjectDescriptionCandidate candidate,
        string? targetColumn, CancellationToken cancellationToken = default) =>
        provider.LoadAsync(connectionString, database, candidate, targetColumn, cancellationToken);

    public Task<ObjectDescriptionSecondaryDetails> LoadSecondaryAsync(
        string connectionString, string database, ObjectDescriptionCandidate candidate,
        CancellationToken cancellationToken = default) =>
        provider.LoadSecondaryAsync(connectionString, database, candidate, cancellationToken);
}

public enum ColumnListPreset { AllVisible, Writable, RequiredInsert, Key, NonLarge }
public enum ColumnListFormat { Horizontal, Vertical, SelectList, QualifiedSelectList, QuotedSelectList, QualifiedQuotedList }

public static class RelationColumnListService
{
    public static IReadOnlySet<int> ApplyPreset(
        IEnumerable<ObjectDescriptionColumn> columns, ColumnListPreset preset)
    {
        var ordered = columns.OrderBy(column => column.Ordinal).ToArray();
        IEnumerable<ObjectDescriptionColumn> selected = preset switch
        {
            ColumnListPreset.Writable => ordered.Where(column => column.IsWritable),
            ColumnListPreset.RequiredInsert => ordered.Where(column => column.IsRequiredInsert),
            ColumnListPreset.Key => KeyColumns(ordered),
            ColumnListPreset.NonLarge => ordered.Where(column => !column.IsObviouslyLarge),
            _ => ordered,
        };
        return selected.Select(column => column.Ordinal).ToHashSet();
    }

    private static IEnumerable<ObjectDescriptionColumn> KeyColumns(ObjectDescriptionColumn[] columns)
    {
        var primary = columns.Where(column => column.IsPrimaryKey).ToArray();
        return primary.Length > 0 ? primary : columns.Where(column => column.IsUnique);
    }
}

public static class ColumnListFormatter
{
    public static string Format(
        IEnumerable<ObjectDescriptionColumn> columns,
        ColumnListFormat format,
        string? alias = null,
        string lineEnding = "\r\n",
        string indentation = "    ")
    {
        var ordered = columns.OrderBy(column => column.Ordinal).ToArray();
        var qualified = format is ColumnListFormat.QualifiedSelectList or ColumnListFormat.QualifiedQuotedList;
        var quoted = format is ColumnListFormat.QuotedSelectList or ColumnListFormat.QualifiedQuotedList;
        var prefix = qualified && !string.IsNullOrWhiteSpace(alias)
            ? SafeAlias(alias) + "."
            : string.Empty;
        var names = ordered.Select(column => prefix
            + (quoted ? PostgreSqlIdentifierQuoter.Quote(column.Name) : column.Name)).ToArray();
        return format switch
        {
            ColumnListFormat.Horizontal => string.Join(", ", names),
            ColumnListFormat.Vertical => string.Join(lineEnding, names),
            _ => indentation + string.Join("," + lineEnding + indentation, names),
        };
    }

    private static string SafeAlias(string value) =>
        Regex.IsMatch(value, "^[a-z_][a-z0-9_$]*$", RegexOptions.CultureInvariant)
            ? value
            : PostgreSqlIdentifierQuoter.Quote(value);
}

public sealed record EditorTextEdit(int Start, int Length, string Replacement, int CaretIndex);

public static class ColumnListInsertionService
{
    public static EditorTextEdit Insert(
        string sql, int selectionStart, int selectionLength, int caretIndex, string formattedList)
    {
        var start = selectionLength > 0 ? selectionStart : caretIndex;
        return new(start, selectionLength, formattedList, start + formattedList.Length);
    }

    public static EditorTextEdit ReplaceWildcard(
        string sql, int caretIndex, string formattedList, string? alias)
    {
        var statementStart = sql.LastIndexOf(';', Math.Max(0, caretIndex - 1)) + 1;
        var statementEnd = sql.IndexOf(';', caretIndex);
        if (statementEnd < 0) statementEnd = sql.Length;
        var statement = sql[statementStart..statementEnd];
        var select = Regex.Match(statement, @"(?is)\bselect\b(?<list>.*?)(?:\bfrom\b|$)");
        if (!select.Success)
            throw new InvalidOperationException("No SELECT list was found in the current statement.");
        var list = select.Groups["list"];
        var pattern = string.IsNullOrWhiteSpace(alias)
            ? @"^\*$"
            : AliasWildcardPattern(alias);
        var matches = TopLevelSelectItems(list.Value)
            .Select(item =>
            {
                var value = item.Value;
                var candidateStart = 0;
                if (item.Start == 0)
                {
                    var prefix = Regex.Match(
                        value, @"^\s*(?:distinct|all)\s+", RegexOptions.IgnoreCase);
                    if (prefix.Success) candidateStart = prefix.Length;
                }
                var candidate = value[candidateStart..];
                var leading = candidate.Length - candidate.TrimStart().Length;
                var trimmed = candidate.Trim();
                var match = Regex.Match(trimmed, pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return match.Success
                    ? new SelectWildcard(item.Start + candidateStart + leading, trimmed.Length)
                    : null;
            })
            .Where(match => match is not null)
            .Cast<SelectWildcard>()
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(matches.Length == 0
                ? "No matching SELECT wildcard was found in the current statement."
                : "More than one matching wildcard exists; select the intended wildcard first.");
        var wildcard = matches[0];
        var lineEnding = sql.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var beforeWildcard = list.Value[..wildcard.Start];
        var lastLineBreak = Math.Max(
            beforeWildcard.LastIndexOf('\n'),
            beforeWildcard.LastIndexOf('\r'));
        var existingIndentation = beforeWildcard[(lastLineBreak + 1)..];
        var wildcardAlreadyOnOwnLine = lastLineBreak >= 0
            && existingIndentation.All(character => character is ' ' or '\t');
        var replacement = wildcardAlreadyOnOwnLine
            ? formattedList.StartsWith(existingIndentation, StringComparison.Ordinal)
                ? formattedList[existingIndentation.Length..]
                : formattedList.TrimStart(' ', '\t')
            : lineEnding + formattedList;
        var absoluteStart = statementStart + list.Index + wildcard.Start;
        return new(absoluteStart, wildcard.Length, replacement,
            absoluteStart + replacement.Length);
    }

    private static string AliasWildcardPattern(string alias)
    {
        var quoted = Regex.Escape(alias.Replace("\"", "\"\"", StringComparison.Ordinal));
        var unquoted = Regex.IsMatch(alias, @"^[\p{L}_][\p{L}\p{N}_$]*$")
            ? $"|{Regex.Escape(alias)}"
            : string.Empty;
        return $@"^(?:""{quoted}""{unquoted})\s*\.\s*\*$";
    }

    private static IReadOnlyList<SelectItem> TopLevelSelectItems(string list)
    {
        var items = new List<SelectItem>();
        var start = 0;
        var depth = 0;
        var singleQuoted = false;
        var doubleQuoted = false;
        for (var index = 0; index < list.Length; index++)
        {
            var character = list[index];
            if (singleQuoted)
            {
                if (character == '\'' && index + 1 < list.Length && list[index + 1] == '\'') index++;
                else if (character == '\'') singleQuoted = false;
                continue;
            }
            if (doubleQuoted)
            {
                if (character == '"' && index + 1 < list.Length && list[index + 1] == '"') index++;
                else if (character == '"') doubleQuoted = false;
                continue;
            }
            if (character == '\'') singleQuoted = true;
            else if (character == '"') doubleQuoted = true;
            else if (character == '(') depth++;
            else if (character == ')' && depth > 0) depth--;
            else if (character == ',' && depth == 0)
            {
                items.Add(new(start, list[start..index]));
                start = index + 1;
            }
        }
        items.Add(new(start, list[start..]));
        return items;
    }

    private sealed record SelectItem(int Start, string Value);
    private sealed record SelectWildcard(int Start, int Length);
}
