using System.Text.RegularExpressions;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Application;

public enum SqlTokenKind { Identifier, Keyword, String, Comment, Punctuation, Other }
public sealed record SqlToken(SqlTokenKind Kind, string Text, int Start, int Length);

public sealed class SqlLexer
{
    public IReadOnlyList<SqlToken> Lex(string sql)
    {
        var result = new List<SqlToken>();
        for (var i = 0; i < sql.Length;)
        {
            var start = i;
            if (char.IsWhiteSpace(sql[i])) { i++; continue; }
            if (sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-') { i += 2; while (i < sql.Length && sql[i] != '\n') i++; result.Add(new(SqlTokenKind.Comment, sql[start..i], start, i - start)); continue; }
            if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*') { i += 2; while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++; i = Math.Min(sql.Length, i + 2); result.Add(new(SqlTokenKind.Comment, sql[start..i], start, i - start)); continue; }
            if (sql[i] is '\'' or '"') { var quote = sql[i++]; while (i < sql.Length) { if (sql[i] == quote && (i + 1 >= sql.Length || sql[i + 1] != quote)) { i++; break; } i += sql[i] == quote ? 2 : 1; } result.Add(new(quote == '\'' ? SqlTokenKind.String : SqlTokenKind.Identifier, sql[start..i], start, i - start)); continue; }
            if (char.IsLetter(sql[i]) || sql[i] == '_') { i++; while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++; var text = sql[start..i]; result.Add(new(Keywords.Contains(text, StringComparer.OrdinalIgnoreCase) ? SqlTokenKind.Keyword : SqlTokenKind.Identifier, text, start, i - start)); continue; }
            result.Add(new(SqlTokenKind.Punctuation, sql[i].ToString(), i++, 1));
        }
        return result;
    }
    public static readonly string[] Keywords = ["SELECT", "FROM", "WHERE", "JOIN", "ON", "GROUP", "BY", "ORDER", "HAVING", "LIMIT", "INSERT", "INTO", "UPDATE", "DELETE", "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "AS", "AND", "OR", "NOT", "NULL", "TRUE", "FALSE", "CASE", "WHEN", "THEN", "ELSE", "END", "RETURNING", "WITH", "UNION", "ALL", "DISTINCT"];
}

public sealed class SqlCompletionEngine : ICompletionService
{
    private readonly SqlLexer _lexer = new();
    public Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(string sql, int caretIndex, DatabaseMetadataSnapshot? metadata, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); caretIndex = Math.Clamp(caretIndex, 0, sql.Length); var prefix = sql[..caretIndex]; var tokens = _lexer.Lex(prefix); var last = tokens.LastOrDefault(); if (last is { Kind: SqlTokenKind.Comment or SqlTokenKind.String } && last.Start + last.Length >= caretIndex) return Task.FromResult<IReadOnlyList<CompletionItem>>([]);
        var match = Regex.Match(prefix, "(?<qual>[A-Za-z_][A-Za-z0-9_]*\\.)?(?<frag>[A-Za-z_][A-Za-z0-9_]*)?$"); var fragment = match.Groups["frag"].Value; var qualifier = match.Groups["qual"].Value.TrimEnd('.'); var items = new List<CompletionItem>(); foreach (var keyword in SqlLexer.Keywords) items.Add(new CompletionItem(keyword, keyword, CompletionKind.Keyword, SortPriority: 10));
        if (metadata is not null)
        {
            foreach (var schema in metadata.Schemas) items.Add(new CompletionItem(Quote(schema), Quote(schema), CompletionKind.Schema));
            foreach (var relation in metadata.Relations.Where(x => string.IsNullOrEmpty(qualifier) || x.SchemaName.Equals(qualifier, StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrEmpty(qualifier)) items.Add(new CompletionItem(Quote(relation.Name), Quote(relation.Name), relation.Kind, relation.SchemaName));
                else foreach (var column in relation.Columns) items.Add(new CompletionItem(Quote(column.Name), Quote(column.Name), CompletionKind.Column, relation.SchemaName, relation.Name));
            }
            foreach (var routine in metadata.Routines) items.Add(new CompletionItem(Quote(routine.Name), Quote(routine.Name), routine.Kind, routine.SchemaName, Detail: routine.Signature));
            foreach (var type in metadata.Types) items.Add(new CompletionItem(Quote(type), Quote(type), CompletionKind.Type));
        }
        var ranked = items.Where(x => x.DisplayText.Trim('"').StartsWith(fragment, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => string.Equals(x.DisplayText.Trim('"'), fragment, StringComparison.Ordinal) ? 3 : string.Equals(x.DisplayText.Trim('"'), fragment, StringComparison.OrdinalIgnoreCase) ? 2 : 1).ThenBy(x => x.DisplayText).Take(100).ToArray(); return Task.FromResult<IReadOnlyList<CompletionItem>>(ranked);
    }
    private static string Quote(string value) => Regex.IsMatch(value, "^[a-z_][a-z0-9_]*$") ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
}
