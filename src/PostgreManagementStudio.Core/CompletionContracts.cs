namespace PostgreManagementStudio.Core;

public enum CompletionKind { Keyword, Schema, Table, View, MaterializedView, Column, Function, Procedure, Type, Sequence, Alias, Cte }
public sealed record CompletionItem(string DisplayText, string InsertionText, CompletionKind Kind, string? Schema = null, string? Parent = null, string? Detail = null, int SortPriority = 0);
public sealed record ColumnMetadata(string Name, string DataType, int OrdinalPosition, bool IsNullable);
public sealed record RelationMetadata(string SchemaName, string Name, CompletionKind Kind, IReadOnlyList<ColumnMetadata> Columns, bool IsSystemObject = false);
public sealed record RoutineMetadata(string SchemaName, string Name, string ReturnType, string Signature, CompletionKind Kind = CompletionKind.Function);
public sealed record DatabaseMetadataSnapshot(string ConnectionKey, string Database, IReadOnlyList<string> Schemas, IReadOnlyList<RelationMetadata> Relations, IReadOnlyList<RoutineMetadata> Routines, IReadOnlyList<string> Types, IReadOnlyList<string> Sequences, DateTimeOffset RefreshedAt);
public interface IPostgresMetadataProvider { Task<DatabaseMetadataSnapshot> LoadAsync(string connectionString, string database, CancellationToken cancellationToken = default); }
public interface ICompletionService { Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(string sql, int caretIndex, DatabaseMetadataSnapshot? metadata, CancellationToken cancellationToken = default); }
