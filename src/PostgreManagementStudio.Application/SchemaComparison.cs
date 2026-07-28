using System.Text;
using System.Text.Json;

namespace PostgreManagementStudio.Application;

public enum SchemaObjectKind { Schema, Table, View, MaterializedView, Sequence, Index, Function, Procedure, Type, Column, Constraint, Trigger }
public enum SchemaDifferenceKind { Identical, Added, Removed, Changed, RenameCandidate, Unresolved, Unsupported, Ignored }
public enum SchemaAction { None, Create, Alter, Drop, Recreate, Rename, Manual }
public enum SchemaRisk { Low, Moderate, High, Destructive, Manual }
public sealed record SchemaObject(string Id, SchemaObjectKind Kind, string Schema, string Name, string? ParentId, string Definition, IReadOnlyDictionary<string, string> Properties);
public sealed record SchemaModel(string Server, string Database, int PostgreSqlMajorVersion, IReadOnlyList<SchemaObject> Objects, IReadOnlyList<string> Warnings, string ModelVersion = "1");
public sealed record SchemaComparisonOptions(bool IncludePrivileges = false, bool IncludeComments = false, bool IncludeOwnership = false, bool CaseSensitiveNames = true, bool IncludeExtensionOwned = false);
public sealed record SchemaDifference(SchemaDifferenceKind Kind, SchemaObject? Source, SchemaObject? Target, SchemaAction Action, SchemaRisk Risk, string Reason, double? RenameConfidence = null);
public sealed record SchemaComparisonResult(SchemaModel Source, SchemaModel Target, IReadOnlyList<SchemaDifference> Differences, DateTimeOffset ComparedAt, bool IsPartial);
public sealed record SchemaDependency(string ObjectId, string DependsOnId);
public sealed record SchemaSynchronisationStep(int Order, SchemaDifference Difference, string Sql, bool TransactionSafe, IReadOnlyList<string> Warnings);
public sealed record SchemaSynchronisationPlan(IReadOnlyList<SchemaSynchronisationStep> Steps, int DestructiveCount, int ManualCount, bool IsSafeToExecute);
public enum SchemaPreviewFilter { All, Additions, Modifications, Deletions, Warnings, Selected }
public sealed record SchemaPreviewItem(SchemaDifference Difference, bool Included, bool IsBlocked, IReadOnlyList<string> Warnings);
public sealed record SchemaSynchronisationPreview(IReadOnlyList<SchemaPreviewItem> Items, IReadOnlyList<SchemaSynchronisationStep> IncludedSteps, string Script, int AdditionCount, int ModificationCount, int DeletionCount, int WarningCount, bool IsPartial, bool HasBlockedChanges)
{
    public IReadOnlyList<SchemaPreviewItem> Filter(SchemaPreviewFilter filter) => filter switch
    {
        SchemaPreviewFilter.Additions => Items.Where(x => x.Difference.Kind == SchemaDifferenceKind.Added).ToArray(),
        SchemaPreviewFilter.Modifications => Items.Where(x => x.Difference.Kind == SchemaDifferenceKind.Changed).ToArray(),
        SchemaPreviewFilter.Deletions => Items.Where(x => x.Difference.Kind == SchemaDifferenceKind.Removed).ToArray(),
        SchemaPreviewFilter.Warnings => Items.Where(x => x.Warnings.Count > 0 || x.IsBlocked).ToArray(),
        SchemaPreviewFilter.Selected => Items.Where(x => x.Included).ToArray(),
        _ => Items
    };
}
public static class SchemaSynchronisationPreviewBuilder
{
    public static SchemaSynchronisationPreview Build(SchemaComparisonResult comparison, IReadOnlyList<SchemaDependency> dependencies, ISet<string>? excluded = null, bool includeDestructive = false)
    {
        excluded ??= new HashSet<string>(StringComparer.Ordinal);
        var plan = SchemaSynchronisationPlanner.Plan(comparison, dependencies, includeDestructive);
        var stepsById = plan.Steps.ToDictionary(x => (x.Difference.Source ?? x.Difference.Target)!.Id);
        var items = comparison.Differences.Where(x => x.Action != SchemaAction.None).Select(d =>
        {
            var id = (d.Source ?? d.Target)!.Id;
            var blocked = d.Action == SchemaAction.Manual || d.Kind is SchemaDifferenceKind.Unresolved or SchemaDifferenceKind.Unsupported;
            var warnings = d.Risk is SchemaRisk.High or SchemaRisk.Destructive ? new[] { d.Reason, "Review this operation before applying it." } : blocked ? new[] { d.Reason } : Array.Empty<string>();
            return new SchemaPreviewItem(d, !excluded.Contains(id) && !blocked && (includeDestructive || d.Risk != SchemaRisk.Destructive), blocked, warnings);
        }).ToArray();
        var included = plan.Steps.Where(s => items.Any(i => i.Included && ReferenceEquals(i.Difference, s.Difference))).ToArray();
        var selectedPlan = plan with { Steps = included };
        var script = SchemaScriptGenerator.Generate(comparison, selectedPlan);
        return new(items, included, script, items.Count(x => x.Difference.Kind == SchemaDifferenceKind.Added), items.Count(x => x.Difference.Kind == SchemaDifferenceKind.Changed), items.Count(x => x.Difference.Kind == SchemaDifferenceKind.Removed), items.Count(x => x.Warnings.Count > 0 || x.IsBlocked), comparison.IsPartial, items.Any(x => x.IsBlocked));
    }
}
public static class SchemaCanonicalizer { public static string Canonicalize(string definition) => string.Join(' ', definition.Replace("\r", " ").Replace("\n", " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)); }
public static class SchemaComparisonService
{
    public static SchemaComparisonResult Compare(SchemaModel source, SchemaModel target, SchemaComparisonOptions options = null!) { options ??= new(); var targetById = target.Objects.ToDictionary(x => x.Id, StringComparer.Ordinal); var differences = new List<SchemaDifference>(); foreach (var s in source.Objects) { if (!targetById.TryGetValue(s.Id, out var t)) { var candidate = target.Objects.FirstOrDefault(x => x.Kind == s.Kind && x.Schema == s.Schema && Fingerprint(x) == Fingerprint(s)); differences.Add(candidate is null ? new(SchemaDifferenceKind.Added, s, null, SchemaAction.Create, RiskFor(SchemaAction.Create, s), "Object exists only in source") : new(SchemaDifferenceKind.RenameCandidate, s, candidate, SchemaAction.Manual, SchemaRisk.Manual, "Equivalent definition under a different identity", 0.8)); } else if (SchemaCanonicalizer.Canonicalize(s.Definition) != SchemaCanonicalizer.Canonicalize(t.Definition) || !s.Properties.OrderBy(x => x.Key).SequenceEqual(t.Properties.OrderBy(x => x.Key))) differences.Add(new(SchemaDifferenceKind.Changed, s, t, ActionFor(s), RiskFor(ActionFor(s), s), "Definition or properties differ")); else differences.Add(new(SchemaDifferenceKind.Identical, s, t, SchemaAction.None, SchemaRisk.Low, "No semantic difference")); } foreach (var t in target.Objects.Where(x => !source.Objects.Any(s => s.Id == x.Id))) differences.Add(new(SchemaDifferenceKind.Removed, null, t, SchemaAction.Drop, SchemaRisk.Destructive, "Object exists only in target")); return new(source, target, differences, DateTimeOffset.UtcNow, source.Warnings.Count > 0 || target.Warnings.Count > 0); }
    private static string Fingerprint(SchemaObject o) => SchemaCanonicalizer.Canonicalize(o.Definition).Replace(o.Name, "", StringComparison.OrdinalIgnoreCase);
    private static SchemaAction ActionFor(SchemaObject s) => s.Kind == SchemaObjectKind.Column ? SchemaAction.Alter : SchemaAction.Alter; private static SchemaRisk RiskFor(SchemaAction a, SchemaObject s) => a switch { SchemaAction.Create => SchemaRisk.Low, SchemaAction.Alter when s.Kind is SchemaObjectKind.Column => SchemaRisk.High, SchemaAction.Alter => SchemaRisk.Moderate, SchemaAction.Drop => SchemaRisk.Destructive, _ => SchemaRisk.Manual };
}
public static class SchemaSynchronisationPlanner
{
    public static SchemaSynchronisationPlan Plan(SchemaComparisonResult comparison, IReadOnlyList<SchemaDependency> dependencies, bool includeDestructive = false) { var selected = comparison.Differences.Where(x => x.Action != SchemaAction.None && (includeDestructive || x.Risk != SchemaRisk.Destructive)).ToArray(); var byId = selected.Select(x => (x.Source ?? x.Target)!.Id).ToHashSet(); var ordered = Topological(selected, dependencies.Where(x => byId.Contains(x.ObjectId) && byId.Contains(x.DependsOnId))); var steps = ordered.Select((d, i) => new SchemaSynchronisationStep(i + 1, d, SchemaScriptGenerator.Statement(d), d.Risk != SchemaRisk.Destructive, d.Risk == SchemaRisk.High ? new[] { "Review locking or rewrite impact before execution." } : Array.Empty<string>())).ToArray(); return new(steps, steps.Count(x => x.Difference.Risk == SchemaRisk.Destructive), steps.Count(x => x.Difference.Action == SchemaAction.Manual), !comparison.IsPartial && steps.All(x => x.Difference.Action != SchemaAction.Manual)); }
    private static IReadOnlyList<SchemaDifference> Topological(IReadOnlyList<SchemaDifference> items, IEnumerable<SchemaDependency> deps) { var map = items.ToDictionary(x => (x.Source ?? x.Target)!.Id); var result = new List<SchemaDifference>(); var remaining = new HashSet<string>(map.Keys); while (remaining.Count > 0) { var next = remaining.Where(id => !deps.Any(d => d.ObjectId == id && remaining.Contains(d.DependsOnId))).ToArray(); if (next.Length == 0) { result.AddRange(remaining.Select(id => map[id])); break; } foreach (var id in next) { result.Add(map[id]); remaining.Remove(id); } } return result; }
}
public static class SchemaScriptGenerator
{
    public static string Generate(SchemaComparisonResult comparison, SchemaSynchronisationPlan plan) { var b = new StringBuilder("-- PostgreManagementStudio Schema Synchronisation\n"); b.AppendLine($"-- Source: {comparison.Source.Server} / {comparison.Source.Database}"); b.AppendLine($"-- Target: {comparison.Target.Server} / {comparison.Target.Database}"); b.AppendLine($"-- Generated: {DateTimeOffset.UtcNow:O}"); if (plan.Steps.Count > 0) b.AppendLine("\nBEGIN;"); foreach (var step in plan.Steps) { b.AppendLine(); b.AppendLine($"-- {step.Difference.Action} {step.Difference.Risk}: {(step.Difference.Source ?? step.Difference.Target)!.Kind} {(step.Difference.Source ?? step.Difference.Target)!.Schema}.{(step.Difference.Source ?? step.Difference.Target)!.Name}"); foreach (var warning in step.Warnings) b.AppendLine($"-- WARNING: {warning}"); b.AppendLine(step.Sql); } if (plan.Steps.Count > 0) b.AppendLine("\nCOMMIT;"); return b.ToString(); }
    public static string Statement(SchemaDifference difference) { var o = difference.Source ?? difference.Target!; var qualified = PostgreSqlIdentifierQuoter.Qualified(o.Schema, o.Name); return difference.Action switch { SchemaAction.Create => o.Definition.TrimEnd(';') + ";", SchemaAction.Alter => o.Definition.TrimEnd(';') + ";", SchemaAction.Drop => $"DROP {o.Kind.ToString().ToUpperInvariant()} {qualified};", _ => $"-- Manual action required for {qualified}" }; }
}
public static class SchemaSnapshotService { public static string Serialize(SchemaModel model) => JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }); public static SchemaModel Deserialize(string json) => JsonSerializer.Deserialize<SchemaModel>(json) ?? throw new InvalidDataException("Invalid schema snapshot."); }
