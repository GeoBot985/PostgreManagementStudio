using System.Text.Json;

namespace PostgreManagementStudio.Application;
public sealed class RecentFilesService
{
    private readonly string _path; private readonly List<string> _files = new(); public int Maximum { get; } = 10; public IReadOnlyList<string> Files => _files;
    public RecentFilesService(string? root = null) { _path = Path.Combine(root ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PostgreManagementStudio", "recent.json"); Load(); }
    public void Add(string path) { var full = Path.GetFullPath(path); _files.RemoveAll(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase)); _files.Insert(0, full); if (_files.Count > Maximum) _files.RemoveRange(Maximum, _files.Count - Maximum); Persist(); }
    public void Remove(string path) { _files.RemoveAll(x => string.Equals(x, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)); Persist(); }
    public void Clear() { _files.Clear(); Persist(); }
    private void Load() { try { if (File.Exists(_path)) _files.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? new()); } catch { _files.Clear(); } }
    private void Persist() { try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); File.WriteAllText(_path, JsonSerializer.Serialize(_files)); } catch { } }
}
