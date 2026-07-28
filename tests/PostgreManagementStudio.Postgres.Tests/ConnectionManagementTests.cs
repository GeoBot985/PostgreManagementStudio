using System.Text.Json;
using Npgsql;
using PostgreManagementStudio.Postgres;

namespace PostgreManagementStudio.Postgres.Tests;

public sealed class ConnectionManagementTests
{
    [Fact]
    public void EffectiveConfigurationNormalisesDefaultsAndRejectsUnsafeProviderOptions()
    {
        var configuration = EffectiveConnectionConfigurationBuilder.FromConnectionString(
            "profile-1",
            " Host = localhost ; Database = postgres ; Username = app ; Password = secret ",
            "PostgreManagementStudio - Test");
        Assert.Equal("localhost", configuration.Profile.Host);
        Assert.Equal(5432, configuration.Profile.Port);
        Assert.Equal(20, configuration.Profile.MaximumPoolSize);
        Assert.Equal(0, configuration.Profile.MinimumPoolSize);
        Assert.Equal(15, configuration.Profile.ConnectionTimeoutSeconds);
        Assert.Equal("PostgreManagementStudio - Test", configuration.Profile.ApplicationName);
        Assert.DoesNotContain("secret", configuration.ToString());

        var reset = Assert.Throws<ConnectionProfileValidationException>(() =>
            EffectiveConnectionConfigurationBuilder.FromConnectionString(
                "unsafe", "Host=localhost;Database=postgres;Username=app;Password=x;No Reset On Close=true", "test"));
        Assert.Contains(reset.Errors, x => x.Field == "No Reset On Close");

        var detail = Assert.Throws<ConnectionProfileValidationException>(() =>
            EffectiveConnectionConfigurationBuilder.FromConnectionString(
                "unsafe", "Host=localhost;Database=postgres;Username=app;Password=x;Include Error Detail=true", "test"));
        Assert.Contains(detail.Errors, x => x.Field == "Include Error Detail");
    }

    [Fact]
    public void EffectiveConfigurationDeepCopiesAdvancedOptions()
    {
        var options = new Dictionary<string, string> { ["Options"] = "-c statement_timeout=1000" };
        var configuration = EffectiveConnectionConfigurationBuilder.Build(
            ValidProfile() with { AdvancedOptions = options });
        options["Options"] = "-c statement_timeout=999999";

        Assert.Equal("-c statement_timeout=1000", configuration.Profile.AdvancedOptions["Options"]);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)configuration.Profile.AdvancedOptions)["Options"] = "changed");
    }

    [Fact]
    public void ValidationReportsFieldsWithoutLeakingSecrets()
    {
        var profile = ValidProfile() with
        {
            Host = " ",
            Port = 70000,
            Username = "\n",
            ConnectionTimeoutSeconds = 0,
            MinimumPoolSize = 5,
            MaximumPoolSize = 2,
            Password = "must-not-leak",
            AdvancedOptions = new Dictionary<string, string> { ["Password"] = "another-secret" },
        };
        var validation = ConnectionProfileValidator.Validate(profile);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, x => x.Field == nameof(profile.Host));
        Assert.Contains(validation.Errors, x => x.Field == nameof(profile.Port));
        Assert.Contains(validation.Errors, x => x.Field == nameof(profile.Username));
        Assert.Contains(validation.Errors, x => x.Field == nameof(profile.AdvancedOptions));
        Assert.DoesNotContain("must-not-leak", string.Join(" ", validation.Errors.Select(x => x.Message)));
        Assert.DoesNotContain("another-secret", string.Join(" ", validation.Errors.Select(x => x.Message)));
    }

    [Fact]
    public void ProfileSerializationAndDisplayNeverContainCredentialMaterial()
    {
        var profile = ValidProfile() with
        {
            Password = "profile-password",
            ClientKey = @"C:\private\client.key",
            ClientCertificatePassword = "certificate-password",
        };
        var json = JsonSerializer.Serialize(profile);
        var display = profile.ToString();
        Assert.DoesNotContain("profile-password", json);
        Assert.DoesNotContain("certificate-password", json);
        Assert.DoesNotContain("client.key", json);
        Assert.DoesNotContain("profile-password", display);
        Assert.DoesNotContain("certificate-password", display);
    }

    [Fact]
    public async Task LifecycleIgnoresStaleAttemptAndConnectDisconnectAreIdempotent()
    {
        var probe = new ControlledProbe();
        var diagnostics = new RecordingDiagnostics();
        await using var lifecycle = new ConnectionLifecycleController("profile-1", probe, diagnostics);
        var configuration = EffectiveConnectionConfigurationBuilder.Build(ValidProfile());
        var first = lifecycle.ConnectAsync(configuration);
        await probe.Started(0).WaitAsync(TimeSpan.FromSeconds(2));
        var firstAttempt = lifecycle.AttemptId;
        Assert.Equal(ManagedConnectionState.Connecting, lifecycle.State);
        Assert.Null(await lifecycle.ConnectAsync(configuration));

        lifecycle.Disconnect();
        lifecycle.Disconnect();
        Assert.Equal(ManagedConnectionState.Disconnected, lifecycle.State);
        var second = lifecycle.ConnectAsync(configuration);
        await probe.Started(1).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotEqual(firstAttempt, lifecycle.AttemptId);
        probe.Complete(0, Success(configuration.Profile.Id));
        Assert.Null(await first);
        Assert.Equal(ManagedConnectionState.Connecting, lifecycle.State);
        probe.Complete(1, Success(configuration.Profile.Id));
        Assert.True((await second)!.Succeeded);
        Assert.Equal(ManagedConnectionState.Connected, lifecycle.State);
        var diagnostic = Assert.Single(diagnostics.Entries);
        Assert.Equal("profile-1", diagnostic.ProfileId);
        Assert.Equal("Connect", diagnostic.Operation);
        Assert.Equal(ManagedConnectionState.Connected, diagnostic.FinalState);
        Assert.DoesNotContain("secret", diagnostic.ToString());

        await lifecycle.DisposeAsync();
        Assert.Equal(ManagedConnectionState.Disposed, lifecycle.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lifecycle.ConnectAsync(configuration));
    }

    [Fact]
    public void RegistryUsesImmutableSnapshotsRejectsDuplicatesAndInvalidatesOnlyChangedProfile()
    {
        var invalidator = new RecordingInvalidator();
        var registry = new ConnectionProfileRegistry(invalidator);
        var original = EffectiveConnectionConfigurationBuilder.Build(ValidProfile());
        registry.Add(original);
        Assert.Throws<InvalidOperationException>(() => registry.Add(
            EffectiveConnectionConfigurationBuilder.Build(ValidProfile() with { Id = "other" })));

        var edited = EffectiveConnectionConfigurationBuilder.Build(original.Profile with { Host = "127.0.0.1" });
        registry.Replace(edited);
        Assert.Single(invalidator.Configurations);
        Assert.Equal(original.Identity, invalidator.Configurations[0].Identity);
        Assert.Equal("localhost", original.Profile.Host);
        Assert.Equal("127.0.0.1", registry.Snapshots.Single().Profile.Host);
        Assert.True(registry.Delete(original.Profile.Id));
        Assert.Equal(2, invalidator.Configurations.Count);
        Assert.False(registry.TryResolve(original.Profile.Id, out _));
    }

    [Fact]
    public void RegistryRejectsApplicationPoolReservationsAboveGlobalCeiling()
    {
        var registry = new ConnectionProfileRegistry(new RecordingInvalidator());
        for (var index = 0; index < 4; index++)
            registry.Add(EffectiveConnectionConfigurationBuilder.Build(
                ValidProfile() with { Id = $"profile-{index}", Name = $"Profile {index}", MaximumPoolSize = 50 }));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Add(EffectiveConnectionConfigurationBuilder.Build(
                ValidProfile() with { Id = "profile-overflow", Name = "Overflow", MaximumPoolSize = 1 })));
        Assert.Contains("200", exception.Message);
    }

    [Theory]
    [InlineData(ConnectionFailureCategory.Authentication, false)]
    [InlineData(ConnectionFailureCategory.Validation, false)]
    [InlineData(ConnectionFailureCategory.Ssl, false)]
    [InlineData(ConnectionFailureCategory.Network, true)]
    [InlineData(ConnectionFailureCategory.ServerUnavailable, true)]
    public void RetryAndResetPoliciesAreSafetyBounded(ConnectionFailureCategory category, bool retry)
    {
        Assert.Equal(retry, ConnectionRetryPolicy.CanRetry(category, operationIsIdempotent: true, retryCount: 0));
        Assert.False(ConnectionRetryPolicy.CanRetry(category, operationIsIdempotent: false, retryCount: 0));
        Assert.False(ConnectionRetryPolicy.CanRetry(category, operationIsIdempotent: true, retryCount: 2));
        Assert.Equal(SessionCleanupDecision.ProviderReset, SessionResetPolicy.Decide(false, false, false, true));
        Assert.Equal(SessionCleanupDecision.DiscardConnection, SessionResetPolicy.Decide(true, false, false, false));
        Assert.Equal(SessionCleanupDecision.DiscardConnection, SessionResetPolicy.Decide(false, true, false, false));
    }

    [Fact]
    public void FailureClassificationKeepsAuthenticationSslDatabaseAndPoolDistinct()
    {
        Assert.Equal(ConnectionFailureCategory.Authentication, ConnectionFailureClassifier.Classify(
            new PostgresException("bad", "ERROR", "ERROR", "28P01")));
        Assert.Equal(ConnectionFailureCategory.DatabaseUnavailable, ConnectionFailureClassifier.Classify(
            new PostgresException("bad", "FATAL", "FATAL", "3D000")));
        Assert.Equal(ConnectionFailureCategory.PoolExhausted, ConnectionFailureClassifier.Classify(
            new NpgsqlException("The connection pool has been exhausted")));
        Assert.Equal(ConnectionFailureCategory.Ssl, ConnectionFailureClassifier.Classify(
            new System.Security.Authentication.AuthenticationException("certificate rejected")));
    }

    private static ConnectionProfile ValidProfile() => new()
    {
        Id = "profile-1",
        Name = "Local test",
        Host = "localhost",
        Database = "postgres",
        Username = "app",
        Password = "secret",
    };

    private static ConnectionTestResult Success(string profileId) => new(
        true, profileId, "PostgreSQL", "postgres", "app", true, false,
        TimeSpan.FromMilliseconds(1), null, "Connected");

    private sealed class RecordingInvalidator : IConnectionPoolInvalidator
    {
        public List<EffectiveConnectionConfiguration> Configurations { get; } = new();
        public void Invalidate(EffectiveConnectionConfiguration configuration) => Configurations.Add(configuration);
    }

    private sealed class RecordingDiagnostics : IConnectionDiagnostics
    {
        public List<ConnectionDiagnostic> Entries { get; } = [];
        public void Record(ConnectionDiagnostic diagnostic) => Entries.Add(diagnostic);
    }

    private sealed class ControlledProbe : IConnectionProbe
    {
        private readonly List<TaskCompletionSource> _started = [];
        private readonly List<TaskCompletionSource<ConnectionTestResult>> _completions = [];
        private readonly object _gate = new();

        public Task Started(int index)
        {
            lock (_gate)
            {
                while (_started.Count <= index) _started.Add(new(TaskCreationOptions.RunContinuationsAsynchronously));
                return _started[index].Task;
            }
        }

        public void Complete(int index, ConnectionTestResult result)
        {
            lock (_gate) _completions[index].TrySetResult(result);
        }

        public Task<ConnectionTestResult> TestAsync(EffectiveConnectionConfiguration configuration, CancellationToken cancellationToken = default)
        {
            TaskCompletionSource started;
            TaskCompletionSource<ConnectionTestResult> completion;
            lock (_gate)
            {
                started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _started.Add(started);
                _completions.Add(completion);
            }
            started.TrySetResult();
            return completion.Task; // Deliberately ignores cancellation to exercise stale result rejection.
        }
    }
}
