using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class SecurityManagementTests
{
    [Fact]
    public void QuotesDifficultIdentifiers()
    {
        Assert.Equal("\"Reporting User\"", PostgreSqlIdentifierQuoter.Quote("Reporting User"));
        Assert.Equal("\"Sales Data\".\"Order\"", PostgreSqlIdentifierQuoter.Qualified("Sales Data", "Order"));
        Assert.Equal("\"a\"\"b\"", PostgreSqlIdentifierQuoter.Quote("a\"b"));
    }

    [Fact]
    public void CreateRoleUsesAttributesAndDoesNotPutPasswordInSql()
    {
        var command = RoleSqlBuilder.Create(new("Reporting User", Login: true, CreateRole: true, Password: "secret"));
        Assert.Contains("CREATE ROLE \"Reporting User\" LOGIN", command.Sql); Assert.Contains("CREATEROLE", command.Sql); Assert.DoesNotContain("secret", command.Sql); Assert.Equal("secret", command.Parameters["password"]);
    }

    [Fact]
    public void PrivilegesAndMembershipsAreSafelyGenerated()
    {
        var target = new PrivilegeTarget(PrivilegeTargetKind.Table, "Order", "Sales Data");
        Assert.Equal("GRANT SELECT, UPDATE ON TABLE \"Sales Data\".\"Order\" TO \"Reporting User\" WITH GRANT OPTION;", PrivilegeSqlBuilder.Grant("Reporting User", target, new[] { SecurityPrivilege.Select, SecurityPrivilege.Update }, true));
        Assert.Contains("WITH ADMIN OPTION", PrivilegeSqlBuilder.Membership("Reporting User", "app role", true, true));
    }

    [Fact]
    public void SafetyRulesRejectInvalidOrHighRiskOperations()
    {
        Assert.Throws<InvalidOperationException>(() => RoleSqlBuilder.Drop("postgres", true));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoleSqlBuilder.Create(new("x", ConnectionLimit: -2)));
        Assert.DoesNotContain("=password", RoleSqlBuilder.Create(new("x", Password: "password")).Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultPrivilegesSupportFutureTables()
    {
        var sql = DefaultPrivilegeSqlBuilder.Grant("owner", PrivilegeTargetKind.Table, "Sales Data", "reader", new[] { SecurityPrivilege.Select });
        Assert.Contains("ALTER DEFAULT PRIVILEGES", sql); Assert.Contains("ON TABLES", sql); Assert.Contains("\"Sales Data\"", sql);
    }
}
