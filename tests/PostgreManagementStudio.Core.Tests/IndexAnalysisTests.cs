using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class IndexAnalysisTests
{
    private static IndexMetadata Index(string name, params string[] keys) => new(1, 10, "public", "orders", name, "btree", keys.Select(x => new IndexKeyDefinition(x)).ToArray(), Array.Empty<string>(), ScanCount: 10);

    [Fact]
    public void SemanticFingerprintIgnoresNamesAndWhitespaceButPreservesMeaning()
    { var a = Index("ix_a", " customer_id "); var b = Index("ix_b", "customer_id"); Assert.Equal(IndexAnalysisService.Fingerprint(a), IndexAnalysisService.Fingerprint(b)); Assert.NotEqual(IndexAnalysisService.Fingerprint(a), IndexAnalysisService.Fingerprint(a with { IsUnique = true })); }

    [Fact]
    public void FindsDuplicatesAndPrefixOverlaps()
    { var one = Index("ix_customer", "customer_id"); var two = Index("ix_customer_created", "customer_id", "created_at"); var duplicate = Index("ix_duplicate", "customer_id"); Assert.Single(IndexAnalysisService.FindDuplicates(new[] { one, duplicate })); Assert.Contains(IndexAnalysisService.FindOverlaps(new[] { one, two }), x => x.Smaller.IndexName == "ix_customer"); }

    [Fact]
    public void ProtectsConstraintIndexesAndGeneratesReviewOnlySql()
    { var protectedIndex = Index("orders_pkey", "id") with { IsPrimary = true, IsConstraintBacked = true }; var duplicate = Index("ix_orders_id", "id"); var recommendations = IndexAnalysisService.Recommend(new[] { protectedIndex, duplicate }, Array.Empty<ForeignKeyMetadata>()); var item = Assert.Single(recommendations, x => x.Category == IndexRecommendationCategory.Duplicate); Assert.Equal(IndexRiskLevel.Destructive, item.Risk); Assert.Contains("DROP INDEX", item.ProposedSql); Assert.Contains("WARNING", IndexAnalysisService.GenerateReviewScript(recommendations)); }

    [Fact]
    public void DetectsForeignKeyGapAndQuotesIdentifiers()
    { var fk = new ForeignKeyMetadata("public", "order items", "fk_customer", new[] { "customer_id" }, "customers", new[] { "id" }); var result = Assert.Single(IndexAnalysisService.Recommend(Array.Empty<IndexMetadata>(), new[] { fk })); Assert.Equal(IndexRecommendationCategory.MissingForeignKey, result.Category); Assert.Contains("\"order items\"", result.ProposedSql); Assert.Contains("CONCURRENTLY", result.ProposedSql); }

    [Fact]
    public void SnapshotHistoryIsBounded()
    { var history = new IndexSnapshotHistory(2); for (var i = 0; i < 3; i++) history.Add(new(DateTimeOffset.UtcNow, null, Array.Empty<IndexMetadata>(), Array.Empty<ForeignKeyMetadata>())); Assert.Equal(2, history.Snapshots.Count); }
}
