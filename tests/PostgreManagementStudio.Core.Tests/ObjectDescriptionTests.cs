using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class ObjectDescriptionTests
{
    private readonly EditorObjectResolver _resolver = new();

    [Fact]
    public void SelectedTextHasPriorityOverCaret()
    {
        const string sql = "SELECT * FROM public.orders JOIN audit.events ON true";
        var start = sql.IndexOf("audit.events", StringComparison.Ordinal);
        var result = _resolver.Resolve(sql, sql.IndexOf("orders", StringComparison.Ordinal),
            start, "audit.events".Length);

        Assert.Equal(["audit", "events"], result!.NameParts);
    }

    [Theory]
    [InlineData("SELECT * FROM public.orders;", "orders", "public", "orders")]
    [InlineData("""SELECT * FROM "Sales"."Order";""", "Order", "Sales", "Order")]
    [InlineData("""SELECT * FROM "a""b"."résumé";""", "résumé", "a\"b", "résumé")]
    public void CaretExtractsCompleteQualifiedIdentifier(
        string sql, string caretText, string first, string second)
    {
        var result = _resolver.Resolve(sql, sql.IndexOf(caretText, StringComparison.Ordinal), 0, 0);

        Assert.Equal([first, second], result!.NameParts);
    }

    [Fact]
    public void CaretAdjacentToIdentifierResolvesIt()
    {
        const string sql = "SELECT * FROM orders ";
        var result = _resolver.Resolve(sql, sql.IndexOf("orders", StringComparison.Ordinal) + 6, 0, 0);

        Assert.Equal("orders", result!.NameParts.Single());
    }

    [Fact]
    public void AliasQualifiedColumnResolvesRelationAndMember()
    {
        const string sql = "SELECT o.customer_id FROM sales.orders AS o WHERE o.customer_id > 0";
        var caret = sql.LastIndexOf("customer_id", StringComparison.Ordinal);
        var result = _resolver.Resolve(sql, caret, 0, 0);

        Assert.Equal(["sales", "orders"], result!.NameParts);
        Assert.Equal("o", result.RelationAlias);
        Assert.Equal("customer_id", result.MemberName);
    }

    [Fact]
    public void AliasWithoutAsAndPartiallyWrittenSqlResolves()
    {
        const string sql = "SELECT x. FROM public.items x\nWHERE";
        var caret = sql.IndexOf('x');
        var result = _resolver.Resolve(sql, caret, caret, 1);

        Assert.Equal(["public", "items"], result!.NameParts);
        Assert.Equal("x", result.RelationAlias);
    }

    [Fact]
    public void RelationTokenRetainsItsStatementAliasForWildcardReplacement()
    {
        const string sql = "SELECT o.* FROM public.orders AS o";
        var caret = sql.IndexOf("orders", StringComparison.Ordinal);
        var result = _resolver.Resolve(sql, caret, 0, 0);

        Assert.Equal(["public", "orders"], result!.NameParts);
        Assert.Equal("o", result.RelationAlias);
        Assert.Null(result.MemberName);
    }

    [Fact]
    public void UnaliasedRelationDoesNotInventAliasForSimpleWildcard()
    {
        const string sql = "SELECT * FROM public.orders;";
        var caret = sql.IndexOf("orders", StringComparison.Ordinal);

        var result = _resolver.Resolve(sql, caret, 0, 0);

        Assert.Equal(["public", "orders"], result!.NameParts);
        Assert.Null(result.RelationAlias);
        var edit = ColumnListInsertionService.ReplaceWildcard(
            sql, caret, "    order_id,\n    status", result.RelationAlias);
        var changed = sql.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        Assert.Equal("SELECT \n    order_id,\n    status FROM public.orders;", changed);
    }

    [Fact]
    public void CteIsMarkedAsEditorLocal()
    {
        const string sql = "WITH recent AS (SELECT 1 AS id) SELECT recent.id FROM recent";
        var caret = sql.IndexOf("recent.id", StringComparison.Ordinal);
        var result = _resolver.Resolve(sql, caret, 0, 0);

        Assert.True(result!.IsEditorLocal);
    }

    [Fact]
    public void RoutineSignatureIsPreserved()
    {
        const string sql = "SELECT public.calculate_total(bigint, numeric)";
        var start = sql.IndexOf("public", StringComparison.Ordinal);
        var result = _resolver.Resolve(sql, start, start, "public.calculate_total(bigint, numeric)".Length);

        Assert.Equal(["public", "calculate_total"], result!.NameParts);
        Assert.Equal("bigint, numeric", result.RoutineSignature);
    }

    [Fact]
    public void PresetsAreDeterministicAndKeepOrdinals()
    {
        var columns = new[]
        {
            Column(3, "payload", nullable: true, dataType: "bytea"),
            Column(1, "id", primary: true, identity: "ALWAYS"),
            Column(2, "required"),
            Column(4, "created_at", defaultExpression: "now()"),
            Column(5, "computed", generated: "id + 1"),
        };

        Assert.Equal([1, 2, 3, 4, 5],
            RelationColumnListService.ApplyPreset(columns, ColumnListPreset.AllVisible).Order());
        Assert.Equal([2, 3, 4],
            RelationColumnListService.ApplyPreset(columns, ColumnListPreset.Writable).Order());
        Assert.Equal([2],
            RelationColumnListService.ApplyPreset(columns, ColumnListPreset.RequiredInsert).Order());
        Assert.Equal([1],
            RelationColumnListService.ApplyPreset(columns, ColumnListPreset.Key).Order());
        Assert.Equal([1, 2, 4, 5],
            RelationColumnListService.ApplyPreset(columns, ColumnListPreset.NonLarge).Order());
    }

    [Theory]
    [InlineData(ColumnListFormat.Horizontal, "order_id, customer_id")]
    [InlineData(ColumnListFormat.Vertical, "order_id\ncustomer_id")]
    [InlineData(ColumnListFormat.SelectList, "    order_id,\n    customer_id")]
    [InlineData(ColumnListFormat.QualifiedSelectList, "    o.order_id,\n    o.customer_id")]
    [InlineData(ColumnListFormat.QuotedSelectList, "    \"order_id\",\n    \"customer_id\"")]
    [InlineData(ColumnListFormat.QualifiedQuotedList, "    o.\"order_id\",\n    o.\"customer_id\"")]
    public void FormatsColumnLists(ColumnListFormat format, string expected)
    {
        var columns = new[] { Column(2, "customer_id"), Column(1, "order_id") };

        Assert.Equal(expected, ColumnListFormatter.Format(columns, format, "o", "\n"));
    }

    [Fact]
    public void FormatterUsesTrustedQuotingForUnsafeAliasAndIdentifiers()
    {
        var result = ColumnListFormatter.Format(
            [Column(1, "a\"b")], ColumnListFormat.QualifiedQuotedList, "Order Alias", "\n");

        Assert.Equal("    \"Order Alias\".\"a\"\"b\"", result);
    }

    [Fact]
    public void WildcardReplacementIsScopedToCurrentStatementAndAlias()
    {
        const string sql = "SELECT * FROM old_items;\r\nSELECT o.*\r\nFROM sales.orders o;";
        var caret = sql.LastIndexOf("orders", StringComparison.Ordinal);
        var edit = ColumnListInsertionService.ReplaceWildcard(
            sql, caret, "    o.id,\r\n    o.name", "o");
        var changed = sql.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);

        Assert.StartsWith("SELECT * FROM old_items;", changed);
        Assert.Contains("SELECT \r\n    o.id,\r\n    o.name\r\nFROM", changed);
    }

    [Fact]
    public void WildcardReplacementRejectsAmbiguousStatement()
    {
        const string sql = "SELECT *, * FROM items";
        Assert.Throws<InvalidOperationException>(() =>
            ColumnListInsertionService.ReplaceWildcard(sql, 8, "    id", null));
    }

    [Theory]
    [InlineData("SELECT count(*) FROM items")]
    [InlineData("SELECT 2 * 3 FROM items")]
    [InlineData("SELECT fn(a, '*') FROM items")]
    public void WildcardReplacementDoesNotChangeExpressions(string sql)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ColumnListInsertionService.ReplaceWildcard(sql, 8, "    id", null));
    }

    [Fact]
    public void WildcardReplacementSupportsQuotedAliasContainingQuote()
    {
        const string sql = "SELECT \"a\"\"b\".* FROM items AS \"a\"\"b\"";
        var edit = ColumnListInsertionService.ReplaceWildcard(
            sql, sql.Length, "    \"a\"\"b\".id", "a\"b");
        var changed = sql.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);

        Assert.Equal("SELECT \n    \"a\"\"b\".id FROM items AS \"a\"\"b\"", changed);
    }

    [Fact]
    public void WildcardReplacementPreservesLfLineEndings()
    {
        const string sql = "SELECT\n    o.*\nFROM orders o";
        var edit = ColumnListInsertionService.ReplaceWildcard(
            sql, sql.IndexOf("orders", StringComparison.Ordinal), "    o.id,\n    o.name", "o");
        var changed = sql.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);

        Assert.DoesNotContain("\r\n", changed);
        Assert.Equal("SELECT\n    o.id,\n    o.name\nFROM orders o", changed);
    }

    [Fact]
    public void InsertReplacesOnlySelectionAndPositionsCaret()
    {
        const string sql = "SELECT placeholder FROM items";
        var start = sql.IndexOf("placeholder", StringComparison.Ordinal);
        var edit = ColumnListInsertionService.Insert(sql, start, "placeholder".Length, start, "id, name");
        var changed = sql.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);

        Assert.Equal("SELECT id, name FROM items", changed);
        Assert.Equal(start + "id, name".Length, edit.CaretIndex);
    }

    private static ObjectDescriptionColumn Column(
        int ordinal,
        string name,
        bool nullable = false,
        string dataType = "integer",
        bool primary = false,
        bool unique = false,
        string identity = "",
        string? defaultExpression = null,
        string? generated = null) =>
        new(ordinal, name, dataType, nullable, defaultExpression, identity, generated,
            null, primary, unique, false, null, null);
}
