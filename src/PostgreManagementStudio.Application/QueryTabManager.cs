namespace PostgreManagementStudio.Application;

public sealed class QueryTabManager
{
    private readonly ResultExecutionService _executionService;
    private int _nextNumber;
    private readonly List<QueryDocument> _documents = new();
    public QueryTabManager(ResultExecutionService executionService) => _executionService = executionService;
    public IReadOnlyList<QueryDocument> Documents => _documents;
    public QueryDocument ActiveDocument { get; private set; } = null!;
    public QueryDocument Open(string? connectionString = null, string database = "postgres")
    { var doc = new QueryDocument(_executionService, $"Query {++_nextNumber}") { ConnectionString = connectionString ?? string.Empty, Database = database }; _documents.Add(doc); ActiveDocument = doc; return doc; }
    public bool TryClose(QueryDocument document, bool discardChanges)
    { if (!_documents.Contains(document)) return false; if (document.IsDirty && !discardChanges) return false; _documents.Remove(document); ActiveDocument = _documents.LastOrDefault()!; return true; }
    public void Activate(QueryDocument document) { if (_documents.Contains(document)) ActiveDocument = document; }
}
