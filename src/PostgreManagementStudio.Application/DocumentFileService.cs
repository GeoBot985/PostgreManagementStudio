using System.Text;

namespace PostgreManagementStudio.Application;

public sealed record LoadedSqlDocument(string Path, string Text, EncodingKind Encoding, DateTimeOffset LastWriteTime, bool IsReadOnly);
public interface IDocumentFileService { Task<LoadedSqlDocument> LoadAsync(string path, CancellationToken cancellationToken = default); Task SaveAsync(SqlDocument document, string path, CancellationToken cancellationToken = default); }
public sealed class DocumentFileService : IDocumentFileService
{
    public async Task<LoadedSqlDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(path); if (!File.Exists(full)) throw new FileNotFoundException("SQL file was not found.", full); var bytes = await File.ReadAllBytesAsync(full, cancellationToken); var (encoding, offset, codec) = Detect(bytes); var text = codec.GetString(bytes, offset, bytes.Length - offset); return new LoadedSqlDocument(full, text, encoding, File.GetLastWriteTimeUtc(full), new FileInfo(full).IsReadOnly);
    }
    public async Task SaveAsync(SqlDocument document, string path, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); if (document.FilePath == full && document.IsReadOnly) throw new UnauthorizedAccessException("The SQL file is read-only; use Save As.");
        Encoding encoding = document.EncodingKind switch { EncodingKind.Utf8Bom => new UTF8Encoding(true), EncodingKind.Utf16LittleEndian => new UnicodeEncoding(false, true), _ => new UTF8Encoding(false) }; var temp = full + ".pms-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, document.Text, encoding, cancellationToken); File.Move(temp, full, true); document.MarkSaved(full, document.EncodingKind, File.GetLastWriteTimeUtc(full)); } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static (EncodingKind, int, Encoding) Detect(byte[] bytes) => bytes is [0xEF, 0xBB, 0xBF, ..] ? (EncodingKind.Utf8Bom, 3, new UTF8Encoding(false, true)) : bytes is [0xFF, 0xFE, ..] ? (EncodingKind.Utf16LittleEndian, 2, new UnicodeEncoding(false, false, true)) : (EncodingKind.Utf8, 0, new UTF8Encoding(false, true));
}
