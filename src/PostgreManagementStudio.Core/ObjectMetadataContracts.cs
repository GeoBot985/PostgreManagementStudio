using System.Diagnostics;
using System.Text.Json.Serialization;

namespace PostgreManagementStudio.Core;

public enum PostgresObjectClass
{
    Database, Schema, Table, PartitionedTable, Partition, View, MaterializedView,
    Sequence, ForeignTable, Index, Function, Procedure, Aggregate, WindowFunction,
    Column, Unknown,
}

public enum MetadataSystemClassification
{
    User, Catalog, InformationSchema, Toast, Temporary, TemporaryToast, ExtensionOwned,
}

public sealed class PostgresObjectIdentity : IEquatable<PostgresObjectIdentity>
{
    public required string ConnectionProfileId { get; init; }
    public required string ConfigurationIdentity { get; init; }
    public required string ServerFingerprint { get; init; }
    public uint DatabaseOid { get; init; }
    public uint ObjectOid { get; init; }
    public required PostgresObjectClass ObjectClass { get; init; }
    public uint? ParentOid { get; init; }
    public uint? SchemaOid { get; init; }
    public int? SubObjectNumber { get; init; }
    public string NameSnapshot { get; init; } = "";

    public bool Equals(PostgresObjectIdentity? other) =>
        other is not null
        && ConnectionProfileId == other.ConnectionProfileId
        && ConfigurationIdentity == other.ConfigurationIdentity
        && ServerFingerprint == other.ServerFingerprint
        && DatabaseOid == other.DatabaseOid
        && ObjectOid == other.ObjectOid
        && ObjectClass == other.ObjectClass
        && ParentOid == other.ParentOid
        && SchemaOid == other.SchemaOid
        && SubObjectNumber == other.SubObjectNumber;

    public override bool Equals(object? obj) => Equals(obj as PostgresObjectIdentity);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ConnectionProfileId);
        hash.Add(ConfigurationIdentity);
        hash.Add(ServerFingerprint);
        hash.Add(DatabaseOid);
        hash.Add(ObjectOid);
        hash.Add(ObjectClass);
        hash.Add(ParentOid);
        hash.Add(SchemaOid);
        hash.Add(SubObjectNumber);
        return hash.ToHashCode();
    }
    public override string ToString() =>
        $"{ConnectionProfileId}:{ServerFingerprint}:{DatabaseOid}:{ObjectClass}:{ObjectOid}:{SubObjectNumber}";
}

public sealed record ObjectMetadataDescriptor(
    PostgresObjectIdentity Identity,
    string Name,
    string? SchemaName,
    string DisplayName,
    string? QualifiedName,
    MetadataSystemClassification SystemClassification,
    bool HasChildren,
    string? RoutineSignature = null,
    uint? ExtensionOid = null,
    int? Ordinal = null);

public sealed record ObjectMetadataBatch(
    PostgresObjectIdentity ParentIdentity,
    IReadOnlyList<ObjectMetadataDescriptor> Objects,
    DateTimeOffset CapturedAt);

public sealed record ObjectMetadataRoot(
    PostgresObjectIdentity DatabaseIdentity,
    string DatabaseName,
    string ServerVersion,
    IReadOnlyList<ObjectMetadataDescriptor> Schemas,
    DateTimeOffset CapturedAt);

public sealed record ObjectMetadataContext
{
    public required string ConnectionProfileId { get; init; }
    public required string ConfigurationIdentity { get; init; }
    [JsonIgnore, DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public required string ConnectionString { get; init; }
    public required string Database { get; init; }
    public bool ShowSystemObjects { get; init; }
    public override string ToString() =>
        $"{ConnectionProfileId}:{ConfigurationIdentity}:{Database}:system={ShowSystemObjects}";
}

public interface IPostgresObjectMetadataProvider
{
    Task<ObjectMetadataRoot> LoadRootAsync(ObjectMetadataContext context, CancellationToken cancellationToken = default);
    Task<ObjectMetadataBatch> LoadChildrenAsync(
        ObjectMetadataContext context,
        PostgresObjectIdentity parent,
        CancellationToken cancellationToken = default);
}
