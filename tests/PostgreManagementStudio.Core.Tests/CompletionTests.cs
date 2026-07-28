using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Core.Tests;

public sealed class CompletionTests
{
    [Fact] public async Task KeywordsWorkWithoutMetadataAndCommentsStringsAreIgnored() { var engine = new SqlCompletionEngine(); Assert.Contains((await engine.GetCompletionsAsync("SEL", 3, null)), x => x.DisplayText == "SELECT"); Assert.Empty(await engine.GetCompletionsAsync("-- SEL", 6, null)); Assert.Empty(await engine.GetCompletionsAsync("SELECT 'SEL", 10, null)); }
    [Fact] public async Task QualifiedColumnsAndQuotedIdentifiersAreRanked() { var metadata = new DatabaseMetadataSnapshot("a", "db", ["sales"], [new RelationMetadata("sales", "Customer Details", CompletionKind.Table, [new ColumnMetadata("Customer ID", "bigint", 1, false)])], [], [], [], DateTimeOffset.UtcNow); var results = await new SqlCompletionEngine().GetCompletionsAsync("SELECT sales.Cust", 17, metadata); Assert.Contains(results, x => x.DisplayText == "\"Customer ID\""); }
    [Fact] public async Task CacheIsolatedByDatabaseAndDeduplicatesLoads() { var provider = new FakeProvider(); var cache = new MetadataCache(provider); await Task.WhenAll(cache.GetAsync("Host=x;Password=p", "a"), cache.GetAsync("Host=x;Password=p", "a")); await cache.GetAsync("Host=x;Password=p", "b"); Assert.Equal(2, provider.Count); }
    private sealed class FakeProvider : IPostgresMetadataProvider { public int Count; public Task<DatabaseMetadataSnapshot> LoadAsync(string c, string d, CancellationToken t = default) { Count++; return Task.FromResult(new DatabaseMetadataSnapshot("key", d, [], [], [], [], [], DateTimeOffset.UtcNow)); } }
}
