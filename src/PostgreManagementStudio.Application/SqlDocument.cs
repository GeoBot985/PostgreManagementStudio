using System.Security.Cryptography;
using System.Text;

namespace PostgreManagementStudio.Application;

public enum SqlDocumentState { Untitled, Saved, Modified, ExternallyModified, DeletedExternally, ReadOnly, Recovered }
public sealed class SqlDocument
{
    public static SqlDocument FromLoaded(LoadedSqlDocument loaded) { var document = new SqlDocument { DisplayName = Path.GetFileName(loaded.Path) }; document.MarkLoaded(loaded.Path, loaded.Text, loaded.Encoding, loaded.LastWriteTime); return document; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DisplayName { get; set; } = "Query";
    public string? FilePath { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public string SavedContentHash { get; private set; } = Hash(string.Empty);
    public EncodingKind EncodingKind { get; private set; } = EncodingKind.Utf8;
    public DateTimeOffset? LastKnownWriteTime { get; private set; }
    public bool IsDirty => Hash(Text) != SavedContentHash;
    public bool IsRecovered { get; private set; }
    public bool IsReadOnly { get; private set; }
    public SqlDocumentState State => IsRecovered ? SqlDocumentState.Recovered : IsReadOnly && IsDirty ? SqlDocumentState.ReadOnly : FilePath is null ? (IsDirty ? SqlDocumentState.Modified : SqlDocumentState.Untitled) : IsDirty ? SqlDocumentState.Modified : SqlDocumentState.Saved;
    public void SetText(string text) => Text = text ?? string.Empty;
    internal void MarkLoaded(string? path, string text, EncodingKind encoding, DateTimeOffset? writeTime, bool recovered = false) { FilePath = path; Text = text; SavedContentHash = Hash(recovered ? string.Empty : text); EncodingKind = encoding; LastKnownWriteTime = writeTime; IsRecovered = recovered; IsReadOnly = path is not null && File.Exists(path) && new FileInfo(path).IsReadOnly; }
    internal void MarkSaved(string path, EncodingKind encoding, DateTimeOffset writeTime) { FilePath = path; SavedContentHash = Hash(Text); EncodingKind = encoding; LastKnownWriteTime = writeTime; IsRecovered = false; IsReadOnly = new FileInfo(path).IsReadOnly; }
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
public enum EncodingKind { Utf8, Utf8Bom, Utf16LittleEndian }
