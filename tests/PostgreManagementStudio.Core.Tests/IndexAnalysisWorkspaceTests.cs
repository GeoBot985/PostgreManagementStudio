using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class IndexAnalysisWorkspaceTests
{
    private static IndexMetadata Index(string name, string method = "btree", string? predicate = null) => new(1, 10, "public", "orders", name, method, new[] { new IndexKeyDefinition("customer_id") }, Array.Empty<string>(), predicate, SizeBytes: 1024, ScanCount: 2);

    [Fact]
    public void ScopeAndSummaryAreDeterministic()
    { var indexes = new[] { Index("ix_a"), Index("ix_b"), Index("ix_gin", "gin") }; var scoped = IndexWorkspaceService.ApplyScope(indexes, new(Schema: "public", Table: "orders")); var summary = IndexWorkspaceService.Summarize(scoped, Array.Empty<ForeignKeyMetadata>()); Assert.Equal(3, summary.TotalIndexes); Assert.Equal(1, summary.OverlapGroups); }

    [Fact]
    public void DatabaseScopeIsContextAndEmptyFiltersAreIgnored()
    {
        var indexes = new[] { Index("ix_a") };
        var scoped = IndexWorkspaceService.ApplyScope(indexes, new(Database: "postgres", Schema: "", Table: "", Index: ""));
        Assert.Same(indexes[0], Assert.Single(scoped));
    }

    [Fact]
    public void NonBtreeIndexesAreNotPrefixOverlaps()
    { var a = Index("gin_a", "gin"); var b = Index("gin_b", "gin"); Assert.Empty(IndexAnalysisService.FindOverlaps(new[] { a, b })); }

    [Fact]
    public void PlanCandidateCarriesEvidenceAndValidatesSafely()
    { var candidate = IndexWorkspaceService.FromPlan(new("Seq Scan", null, "public", "orders", null, null, null, null, null, null, null, Array.Empty<ExecutionPlanNode>(), new Dictionary<string, System.Text.Json.JsonElement>()), "public", "orders", new[] { "customer_id" }, new[] { new IndexEvidence("plan", "select 1", "node", "large filter") }); var validation = IndexWorkspaceService.Validate(candidate, Array.Empty<IndexMetadata>()); Assert.True(validation.IsValid); Assert.Contains("CONCURRENTLY", candidate.SqlPreview); Assert.Contains("evidence", string.Join(' ', candidate.Limitations), StringComparison.OrdinalIgnoreCase); }

    [Fact]
    public void CandidateValidationRejectsVolatilePredicateAndDuplicateKeys()
    { var candidate = new MissingIndexCandidate("public", "orders", new[] { "id", "id" }, Array.Empty<string>(), "created_at > now()", "btree", RecommendationConfidence.Low, Array.Empty<IndexEvidence>(), "", Array.Empty<string>()); var result = IndexWorkspaceService.Validate(candidate, Array.Empty<IndexMetadata>()); Assert.False(result.IsValid); Assert.Contains(result.Errors, x => x.Contains("unique")); Assert.Contains(result.Errors, x => x.Contains("Volatile")); }
}
