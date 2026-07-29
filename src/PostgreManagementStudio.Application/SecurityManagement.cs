using System.Diagnostics;
using System.Text;

namespace PostgreManagementStudio.Application;

public sealed record PostgreSqlRole(string Name, bool CanLogin, bool IsSuperuser, bool CanCreateDatabase, bool CanCreateRole, bool CanReplicate, bool BypassRowLevelSecurity, bool Inherit, int ConnectionLimit, DateTimeOffset? PasswordExpiry, string? Comment);
public sealed record RoleMembership(string Member, string GrantedRole, bool WithAdminOption, bool IsInherited);
public enum SecurityPrivilege { Connect, Create, Temporary, Usage, Select, Insert, Update, Delete, Truncate, References, Trigger, Execute, UsageSequence }
public enum PrivilegeTargetKind { Database, Schema, Table, View, MaterializedView, Sequence, Function, Procedure }
public sealed record PrivilegeTarget(PrivilegeTargetKind Kind, string Name, string? Schema = null, string? Signature = null);
public sealed record RoleDefinition(string Name, bool Login = false, bool Superuser = false, bool CreateDatabase = false, bool CreateRole = false, bool Replication = false, bool BypassRls = false, bool Inherit = true, int ConnectionLimit = -1, [property: DebuggerBrowsable(DebuggerBrowsableState.Never)] string? Password = null, DateTimeOffset? PasswordExpiry = null, string? Comment = null)
{
    public override string ToString() => $"RoleDefinition ({Name}, password redacted)";
}
public sealed record SecuritySqlCommand(string Sql, IReadOnlyDictionary<string, object?> Parameters);

public static class PostgreSqlIdentifierQuoter
{
    public static string Quote(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        if (identifier.IndexOf('\0') >= 0) throw new ArgumentException("PostgreSQL identifiers cannot contain null characters.", nameof(identifier));
        return '"' + identifier.Replace("\"", "\"\"") + '"';
    }
    public static string Qualified(string? schema, string name) => schema is null ? Quote(name) : Quote(schema) + "." + Quote(name);
    public static string Qualified(params string[] components)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Length == 0 || components.Any(string.IsNullOrEmpty)) throw new ArgumentException("At least one non-empty identifier component is required.", nameof(components));
        return string.Join(".", components.Select(Quote));
    }
}
public static class RoleSqlBuilder
{
    public static SecuritySqlCommand Create(RoleDefinition role)
    { Validate(role); var parameters = Parameters(role); var sql = new StringBuilder("CREATE ROLE ").Append(PostgreSqlIdentifierQuoter.Quote(role.Name)); AddAttributes(sql, role); if (role.Password is not null) sql.Append(" PASSWORD @password"); sql.Append(';'); return new(sql.ToString(), parameters); }
    public static SecuritySqlCommand Alter(RoleDefinition role)
    { Validate(role); var parameters = Parameters(role); var sql = new StringBuilder("ALTER ROLE ").Append(PostgreSqlIdentifierQuoter.Quote(role.Name)); AddAttributes(sql, role); if (role.Password is not null) sql.Append(" PASSWORD @password"); sql.Append(';'); return new(sql.ToString(), parameters); }
    public static string Drop(string role, bool currentUser) => currentUser ? throw new InvalidOperationException("The active session role cannot be dropped.") : $"DROP ROLE {PostgreSqlIdentifierQuoter.Quote(role)};";
    private static void AddAttributes(StringBuilder sql, RoleDefinition r) { sql.Append(r.Login ? " LOGIN" : " NOLOGIN").Append(r.Superuser ? " SUPERUSER" : " NOSUPERUSER").Append(r.CreateDatabase ? " CREATEDB" : " NOCREATEDB").Append(r.CreateRole ? " CREATEROLE" : " NOCREATEROLE").Append(r.Replication ? " REPLICATION" : " NOREPLICATION").Append(r.BypassRls ? " BYPASSRLS" : " NOBYPASSRLS").Append(r.Inherit ? " INHERIT" : " NOINHERIT").Append(" CONNECTION LIMIT ").Append(r.ConnectionLimit); if (r.PasswordExpiry is not null) sql.Append(" VALID UNTIL @password_expiry"); }
    private static IReadOnlyDictionary<string, object?> Parameters(RoleDefinition role)
    { var values = new Dictionary<string, object?>(); if (role.Password is not null) values["password"] = role.Password; if (role.PasswordExpiry is { } expiry) values["password_expiry"] = expiry.UtcDateTime; return values; }
    private static void Validate(RoleDefinition r) { if (string.IsNullOrWhiteSpace(r.Name)) throw new ArgumentException("Role name is required."); if (r.ConnectionLimit < -1) throw new ArgumentOutOfRangeException(nameof(r.ConnectionLimit)); }
}
public static class PrivilegeSqlBuilder
{
    public static string Grant(string grantee, PrivilegeTarget target, IEnumerable<SecurityPrivilege> privileges, bool grantOption = false) => Build("GRANT", grantee, target, privileges, grantOption);
    public static string Revoke(string grantee, PrivilegeTarget target, IEnumerable<SecurityPrivilege> privileges, bool grantOption = false) => Build(grantOption ? "REVOKE GRANT OPTION FOR" : "REVOKE", grantee, target, privileges, false);
    public static string Membership(string member, string grantedRole, bool grant, bool adminOption = false) => grant ? $"GRANT {PostgreSqlIdentifierQuoter.Quote(grantedRole)} TO {PostgreSqlIdentifierQuoter.Quote(member)}" + (adminOption ? " WITH ADMIN OPTION;" : ";") : $"REVOKE {PostgreSqlIdentifierQuoter.Quote(grantedRole)} FROM {PostgreSqlIdentifierQuoter.Quote(member)};";
    private static string Build(string action, string grantee, PrivilegeTarget target, IEnumerable<SecurityPrivilege> privileges, bool grantOption) { var names = privileges.Select(x => x switch { SecurityPrivilege.UsageSequence => "USAGE", _ => x.ToString().ToUpperInvariant() }); var targetName = target.Kind == PrivilegeTargetKind.Database ? "DATABASE " + PostgreSqlIdentifierQuoter.Quote(target.Name) : target.Kind == PrivilegeTargetKind.Schema ? "SCHEMA " + PostgreSqlIdentifierQuoter.Quote(target.Name) : target.Kind switch { PrivilegeTargetKind.Function => "FUNCTION " + PostgreSqlIdentifierQuoter.Qualified(target.Schema, target.Name) + "(" + SafeRoutineSignature(target.Signature) + ")", PrivilegeTargetKind.Procedure => "PROCEDURE " + PostgreSqlIdentifierQuoter.Qualified(target.Schema, target.Name) + "(" + SafeRoutineSignature(target.Signature) + ")", _ => "TABLE " + PostgreSqlIdentifierQuoter.Qualified(target.Schema, target.Name) }; return $"{action} {string.Join(", ", names)} ON {targetName} TO {PostgreSqlIdentifierQuoter.Quote(grantee)}" + (grantOption ? " WITH GRANT OPTION" : "") + ";"; }
    private static string SafeRoutineSignature(string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature)) return "";
        if (signature.Length > 2048 || signature.Any(char.IsControl)
            || signature.Contains(';') || signature.Contains("--", StringComparison.Ordinal)
            || signature.Contains("/*", StringComparison.Ordinal) || signature.Contains('\''))
            throw new ArgumentException("Routine signature contains unsafe SQL syntax.", nameof(signature));
        return signature;
    }
}
public static class DefaultPrivilegeSqlBuilder
{
    public static string Grant(string owner, PrivilegeTargetKind objectKind, string schema, string grantee, IEnumerable<SecurityPrivilege> privileges) { var type = objectKind switch { PrivilegeTargetKind.Table or PrivilegeTargetKind.View or PrivilegeTargetKind.MaterializedView => "TABLES", PrivilegeTargetKind.Sequence => "SEQUENCES", PrivilegeTargetKind.Function or PrivilegeTargetKind.Procedure => "FUNCTIONS", _ => throw new ArgumentException("Default privileges require a relation, sequence, or routine target.") }; return $"ALTER DEFAULT PRIVILEGES FOR ROLE {PostgreSqlIdentifierQuoter.Quote(owner)} IN SCHEMA {PostgreSqlIdentifierQuoter.Quote(schema)} GRANT {string.Join(", ", privileges.Select(x => x == SecurityPrivilege.UsageSequence ? "USAGE" : x.ToString().ToUpperInvariant()))} ON {type} TO {PostgreSqlIdentifierQuoter.Quote(grantee)};"; }
}
public static class SecurityMetadataQueries
{
    public const string Roles = "SELECT rolname,rolcanlogin,rolsuper,rolcreatedb,rolcreaterole,rolreplication,rolbypassrls,rolinherit,rolconnlimit,rolvaliduntil,COALESCE(obj_description(oid,'pg_authid'),'') FROM pg_roles ORDER BY rolname";
    public const string Memberships = "SELECT member.rolname, granted.rolname, m.admin_option FROM pg_auth_members m JOIN pg_roles member ON member.oid=m.member JOIN pg_roles granted ON granted.oid=m.roleid ORDER BY member.rolname,granted.rolname";
    public const string Databases = "SELECT datname FROM pg_database ORDER BY datname";
    public const string Schemas = "SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE 'pg_%' AND nspname <> 'information_schema' ORDER BY nspname";
    public const string CurrentUser = "SELECT current_user";
}
public sealed record SecurityAuditEntry(DateTimeOffset Timestamp, string Server, string ActingUser, string Operation, string Target, string SanitisedSql, bool Success, bool Verified, TimeSpan Elapsed);
