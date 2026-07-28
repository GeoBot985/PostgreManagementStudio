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
