namespace PostgreManagementStudio.IntegrationTests;

public class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING")))
            Skip = "PMS_CONNECTION_STRING is required for PostgreSQL integration tests.";
    }
}

public sealed class PerformanceFactAttribute : PostgreSqlFactAttribute
{
    public PerformanceFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PMS_RUN_PERF"), "1", StringComparison.Ordinal))
            Skip = "Set PMS_RUN_PERF=1 to run performance tests.";
    }
}

public class SeededPostgreSqlFactAttribute : PostgreSqlFactAttribute
{
    public SeededPostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PMS_TEST_DATABASE")))
            Skip = "Run scripts/test-release.ps1 to create the isolated seeded PostgreSQL environment.";
    }
}

public sealed class ExternalToolsFactAttribute : SeededPostgreSqlFactAttribute
{
    public ExternalToolsFactAttribute()
    {
        var directory = Environment.GetEnvironmentVariable("PMS_TEST_PG_BIN");
        if (string.IsNullOrWhiteSpace(directory) ||
            !File.Exists(Path.Combine(directory, "pg_dump.exe")) ||
            !File.Exists(Path.Combine(directory, "pg_restore.exe")))
            Skip = "PostgreSQL external tools were not configured by the release regression environment.";
    }
}
