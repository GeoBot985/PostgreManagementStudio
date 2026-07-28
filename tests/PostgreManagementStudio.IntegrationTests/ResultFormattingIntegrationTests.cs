using PostgreManagementStudio.Core;
using PostgreManagementStudio.Postgres;
using PostgreManagementStudio.Results;

namespace PostgreManagementStudio.IntegrationTests;

public sealed class ResultFormattingIntegrationTests
{
    [PostgreSqlFact]
    public async Task MixedPostgresValuesSerializeInAllFormats()
    {
        var connection = Environment.GetEnvironmentVariable("PMS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("PostgreSQL test was executed without PMS_CONNECTION_STRING.");
        var request = new QueryRequest("SELECT 1::integer AS integer_value, 1234567890.123456789::numeric AS precise_numeric, NULL::text AS null_value, ''::text AS empty_string, E'a\\tb' AS tabbed_text, 'He said \"hello\"'::text AS quoted_text, decode('4d5a', 'hex') AS binary_value;", connection);
        await using var session = await new ResultSessionBuilder(new NpgsqlQueryExecutor()).ExecuteAndBuildAsync(request, CancellationToken.None);
        var store = session.ResultSets.Single(); var selection = new ResultSelection(0, 0, 0, store.Schema.Columns.Count - 1);
        foreach (var format in Enum.GetValues<ResultSerializationFormat>())
        {
            using var writer = new StringWriter(); var serializer = new ResultSerializer(new DefaultResultValueFormatter(), format);
            var outcome = await serializer.SerializeAsync(store, selection, new(format, MaximumOutputCharacters: 100_000), writer);
            Assert.True(outcome.Completed); Assert.Contains("NULL", writer.ToString()); Assert.NotEmpty(writer.ToString());
        }
    }
}
