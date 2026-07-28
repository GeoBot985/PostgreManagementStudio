using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class ObjectSearchTests
{
    [Fact]
    public void ConvertsWildcardsAndEscapesLikeCharacters()
    {
        Assert.Equal("%order%", ObjectSearchQueryBuilder.ToLikePattern("order")); Assert.Equal("%%%", ObjectSearchQueryBuilder.ToLikePattern("*")); Assert.Equal("%100\\%\\_done%", ObjectSearchQueryBuilder.ToLikePattern("100%_done"));
    }

    [Fact]
    public void BuildsParameterizedTypeFilteredSearchAndExcludesSystemSchemas()
    {
        var query = ObjectSearchQueryBuilder.Build(new("customer", new HashSet<SearchObjectType> { SearchObjectType.Table }, MaximumResults: 25)); Assert.Contains("NOT LIKE 'pg_%'", query.Sql); Assert.Contains("c.relkind = ANY(@relkinds)", query.Sql); Assert.Equal(25, query.Parameters["limit"]); Assert.DoesNotContain("customer", query.Sql);
    }

    [Fact]
    public void HistoryIsBoundedAndDuplicateSearchesMoveToFront()
    {
        var history = new ObjectSearchHistoryService(2); history.Add(new("one", null, null, false, DateTimeOffset.UtcNow)); history.Add(new("two", null, null, false, DateTimeOffset.UtcNow)); history.Add(new("one", null, null, false, DateTimeOffset.UtcNow)); Assert.Equal(2, history.Entries.Count); Assert.Equal("one", history.Entries[0].Text); history.Clear(); Assert.Empty(history.Entries);
    }

    [Fact]
    public void DeduplicatesResultsByDatabaseSchemaNameAndType()
    {
        var r = new ObjectSearchResult(SearchObjectType.Table, "db", "public", "orders", null, "Name", null); Assert.Single(ObjectSearchResultUtilities.Deduplicate(new[] { r, r }));
    }
}
