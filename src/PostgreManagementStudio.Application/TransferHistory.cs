namespace PostgreManagementStudio.Application;

public sealed record TransferHistoryEntry(DateTimeOffset Started, DateTimeOffset Completed, string Operation, string Source, string Destination, string Status, long RowsRead, long RowsWritten, long RowsRejected, string? OutputPath, IReadOnlyList<string> Errors);

public sealed class TransferHistoryService(int maximumEntries = 100)
{
    private readonly LinkedList<TransferHistoryEntry> _entries = new();
    private readonly object _gate = new();
    public IReadOnlyList<TransferHistoryEntry> Entries { get { lock (_gate) return _entries.ToArray(); } }
    public void Add(TransferHistoryEntry entry) { lock (_gate) { _entries.AddFirst(entry with { Errors = entry.Errors.Take(20).Select(error => PostgreManagementStudio.Core.SensitiveDataRedactor.Redact(error)).ToArray() }); while (_entries.Count > Math.Max(1, maximumEntries)) _entries.RemoveLast(); } }
    public void Remove(TransferHistoryEntry entry) { lock (_gate) _entries.Remove(entry); }
    public void Clear() { lock (_gate) _entries.Clear(); }
}
