namespace PostgreManagementStudio.Application;

public sealed record SearchOptions(bool MatchCase = false, bool WholeWord = false, bool Wrap = true);

public sealed class FindReplaceService
{
    public int FindNext(string text, string search, int start, SearchOptions options) => Find(text, search, Math.Clamp(start, 0, text.Length), options, false);
    public int FindPrevious(string text, string search, int start, SearchOptions options)
    {
        Validate(search); var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase; var last = -1;
        for (var i = text.IndexOf(search, 0, comparison); i >= 0 && i <= Math.Clamp(start, 0, text.Length); i = text.IndexOf(search, i + 1, comparison)) if (IsWholeWord(text, search, i, options)) last = i;
        return last >= 0 || !options.Wrap ? last : FindPrevious(text, search, text.Length, options with { Wrap = false });
    }
    public string ReplaceAll(string text, string search, string replacement, SearchOptions options, out int count)
    {
        Validate(search); count = 0; var result = text; var position = 0; var forward = options with { Wrap = false };
        while (position <= result.Length) { var found = FindNext(result, search, position, forward); if (found < 0) break; result = result.Remove(found, search.Length).Insert(found, replacement); position = found + replacement.Length; count++; }
        return result;
    }
    public string ReplaceCurrent(string text, string search, string replacement, int index, SearchOptions options, out bool replaced) { replaced = index >= 0 && IsWholeWord(text, search, index, options); return replaced ? text.Remove(index, search.Length).Insert(index, replacement) : text; }
    private static int Find(string text, string search, int start, SearchOptions options, bool previous) { Validate(search); var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase; for (var i = text.IndexOf(search, start, comparison); i >= 0; i = text.IndexOf(search, i + 1, comparison)) if (IsWholeWord(text, search, i, options)) return i; return options.Wrap ? Find(text, search, 0, options with { Wrap = false }, previous) : -1; }
    private static bool IsWholeWord(string text, string search, int index, SearchOptions options) => !options.WholeWord || (index == 0 || !char.IsLetterOrDigit(text[index - 1])) && (index + search.Length == text.Length || !char.IsLetterOrDigit(text[index + search.Length]));
    private static void Validate(string search) { if (string.IsNullOrEmpty(search)) throw new ArgumentException("Search text is required.", nameof(search)); }
}
