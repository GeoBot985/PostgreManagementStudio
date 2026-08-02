using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace PostgreManagementStudio.Desktop;

public sealed class SqlEditorControl : TextEditor
{
    public SqlEditorControl()
    {
        ShowLineNumbers = true;
        Options.EnableRectangularSelection = true;
        Options.IndentationSize = 4;
        Options.ConvertTabsToSpaces = false;
        TextArea.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, new RoutedEventArgs());
        SetTheme(false);
    }

    public event RoutedEventHandler? SelectionChanged;
    public int CaretIndex { get => CaretOffset; set => CaretOffset = Math.Clamp(value, 0, Text.Length); }
    public new string SelectedText
    {
        get => base.SelectedText;
        set
        {
            var start = SelectionStart;
            Document.Replace(start, SelectionLength, value);
            Select(start, value.Length);
        }
    }

    public void SetTheme(bool dark)
    {
        Background = Brush(dark ? "#1E1E1E" : "#FFFFFF");
        Foreground = Brush(dark ? "#DCDCDC" : "#1F1F1F");
        LineNumbersForeground = Brush(dark ? "#858585" : "#6B7280");
        SyntaxHighlighting = LoadHighlighting(dark);
    }

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));

    private static IHighlightingDefinition LoadHighlighting(bool dark)
    {
        var keyword = dark ? "#569CD6" : "#0000FF";
        var comment = dark ? "#6A9955" : "#008000";
        var literal = dark ? "#CE9178" : "#A31515";
        var number = dark ? "#B5CEA8" : "#098658";
        var function = dark ? "#DCDCAA" : "#795E26";
        var xml = $@"<?xml version='1.0'?>
<SyntaxDefinition name='PostgreSQL' extensions='.sql' xmlns='http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008'>
  <Color name='Keyword' foreground='{keyword}' fontWeight='bold'/><Color name='Comment' foreground='{comment}'/>
  <Color name='String' foreground='{literal}'/><Color name='Number' foreground='{number}'/><Color name='Function' foreground='{function}'/>
  <RuleSet ignoreCase='true'>
  <Rule color='Comment'>--.*$</Rule><Span color='Comment' begin='/\*' end='\*/'/>
  <Span color='String' begin='&apos;' end='&apos;'/><Rule color='Number'>\b[0-9]+(\.[0-9]+)?\b</Rule>
  <Keywords color='Keyword'>
    <Word>SELECT</Word><Word>FROM</Word><Word>WHERE</Word><Word>JOIN</Word><Word>LEFT</Word><Word>RIGHT</Word><Word>FULL</Word><Word>INNER</Word><Word>OUTER</Word><Word>ON</Word><Word>AS</Word><Word>AND</Word><Word>OR</Word><Word>NOT</Word><Word>NULL</Word><Word>TRUE</Word><Word>FALSE</Word><Word>INSERT</Word><Word>INTO</Word><Word>VALUES</Word><Word>UPDATE</Word><Word>SET</Word><Word>DELETE</Word><Word>CREATE</Word><Word>ALTER</Word><Word>DROP</Word><Word>TABLE</Word><Word>VIEW</Word><Word>INDEX</Word><Word>FUNCTION</Word><Word>PROCEDURE</Word><Word>RETURNS</Word><Word>BEGIN</Word><Word>END</Word><Word>CASE</Word><Word>WHEN</Word><Word>THEN</Word><Word>ELSE</Word><Word>DISTINCT</Word><Word>GROUP</Word><Word>BY</Word><Word>ORDER</Word><Word>HAVING</Word><Word>LIMIT</Word><Word>OFFSET</Word><Word>UNION</Word><Word>ALL</Word><Word>WITH</Word><Word>RECURSIVE</Word><Word>PRIMARY</Word><Word>KEY</Word><Word>FOREIGN</Word><Word>REFERENCES</Word><Word>UNIQUE</Word><Word>DEFAULT</Word><Word>CONSTRAINT</Word><Word>RETURNING</Word><Word>CONCURRENTLY</Word><Word>USING</Word><Word>EXPLAIN</Word><Word>ANALYZE</Word><Word>VACUUM</Word><Word>DO</Word><Word>DECLARE</Word><Word>LOOP</Word><Word>IF</Word><Word>ELSIF</Word><Word>LANGUAGE</Word>
  </Keywords>
  <Keywords color='Function'><Word>COUNT</Word><Word>SUM</Word><Word>AVG</Word><Word>MIN</Word><Word>MAX</Word><Word>COALESCE</Word><Word>NULLIF</Word><Word>NOW</Word><Word>CURRENT_DATE</Word><Word>CURRENT_TIMESTAMP</Word><Word>STRING_AGG</Word><Word>ARRAY_AGG</Word><Word>JSON_AGG</Word></Keywords>
  </RuleSet>
</SyntaxDefinition>";
        using var reader = XmlReader.Create(new StringReader(xml));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
