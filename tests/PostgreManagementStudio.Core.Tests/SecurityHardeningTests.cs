using System.Text.Json;
using PostgreManagementStudio.Application;
using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Core.Tests;

public sealed class SecurityHardeningTests
{
    [Theory]
    [InlineData("Host=db;Password=seed-secret;Username=u")]
    [InlineData("postgresql://user:seed-secret@localhost/db")]
    [InlineData("Authorization: Bearer seed-secret")]
    [InlineData("client_secret = 'seed-secret'")]
    public void CentralRedactor_RemovesKnownCredentialShapes(string input)
    {
        var output = SensitiveDataRedactor.Redact(input, ["seed-secret"]);
        Assert.DoesNotContain("seed-secret", output, StringComparison.Ordinal);
        Assert.Contains(SensitiveDataRedactor.Replacement, output);
    }

    [Fact]
    public void StructuredAndNestedExceptionRedaction_IsRecursive()
    {
        var exception = new InvalidOperationException("Password=outer-secret",
            new Exception("postgresql://u:inner-secret@host/db"));
        var properties = SensitiveDataRedactor.RedactProperties(new Dictionary<string, object?>
        {
            ["password"] = "property-secret",
            ["exception"] = exception,
            ["nested"] = new Dictionary<string, object?> { ["access_token"] = "token-secret" },
        });
        var json = JsonSerializer.Serialize(properties);
        Assert.DoesNotContain("outer-secret", json);
        Assert.DoesNotContain("inner-secret", json);
        Assert.DoesNotContain("property-secret", json);
        Assert.DoesNotContain("token-secret", json);
    }

    [Fact]
    public void HostileMetadata_IsBoundedAndControlCharactersAreNeutralised()
    {
        var payload = "table\r\nDROP TABLE users;\u202E" + new string('x', 1_000);
        var safe = UntrustedText.ForDisplay(payload, 80);
        Assert.DoesNotContain('\r', safe);
        Assert.DoesNotContain('\n', safe);
        Assert.DoesNotContain('\u202E', safe);
        Assert.True(safe.Length <= 81);
    }

    [Theory]
    [InlineData("../database", "_database")]
    [InlineData(@"schema\table", "schema_table")]
    [InlineData(".", "export")]
    public void DatabaseNames_CannotTraverseGeneratedFileNames(string value, string expected)
        => Assert.Equal(expected, UntrustedText.SafeFileName(value));

    [Fact]
    public async Task CredentialLifecycle_DoesNotDuplicateOrRetainSecretsImplicitly()
    {
        var store = new MemoryCredentialStore();
        var lifecycle = new CredentialLifecycleService(store);
        var secret = "credential-value".ToCharArray();
        var reference = await lifecycle.SaveAsync("profile-a", secret);
        Assert.Null(await lifecycle.DuplicateAsync(reference, "profile-b", false));
        var duplicate = await lifecycle.DuplicateAsync(reference, "profile-b", true);
        Assert.NotNull(duplicate);
        await lifecycle.DeleteAsync(reference);
        Assert.Null(await lifecycle.RetrieveAsync(reference));
    }

    [Fact]
    public async Task MalformedSettings_AreBackedUpAndDefaultsAreSecure()
    {
        var root = Path.Combine(Path.GetTempPath(), "pms-security-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, "{ malformed");
            var result = await new JsonApplicationSettingsStore(path).LoadAsync();
            Assert.Equal(ApplicationSettings.CurrentVersion, result.Settings.Version);
            Assert.Equal(QueryTextStorageMode.FingerprintAndPreview, result.Settings.QueryHistoryTextMode);
            Assert.Single(Directory.GetFiles(root, "*.bak"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SettingsValidation_RejectsInvalidEnumsAndBoundsUntrustedValues()
    {
        var settings = new ApplicationSettings
        {
            DefaultDatabase = new string('x', 1_000),
            QueryHistoryTextMode = (QueryTextStorageMode)999,
            QueryHistoryRetentionDays = int.MaxValue,
            QueryHistoryMaximumPerQuery = -10,
        }.Validate();
        Assert.Equal(255, settings.DefaultDatabase.Length);
        Assert.Equal(QueryTextStorageMode.FingerprintAndPreview, settings.QueryHistoryTextMode);
        Assert.Equal(3650, settings.QueryHistoryRetentionDays);
        Assert.Equal(1, settings.QueryHistoryMaximumPerQuery);
    }

    [Fact]
    public void SettingsMigration_DropsLegacyUnknownCredentialFields()
    {
        using var password = JsonDocument.Parse("\"legacy-secret\"");
        using var future = JsonDocument.Parse("\"preserved\"");
        var settings = new ApplicationSettings
        {
            AdditionalValues = new()
            {
                ["databasePassword"] = password.RootElement.Clone(),
                ["futureSetting"] = future.RootElement.Clone(),
            },
        }.Validate();
        Assert.DoesNotContain("databasePassword", settings.AdditionalValues);
        Assert.Contains("futureSetting", settings.AdditionalValues);
    }

    [Fact]
    public void IdentifierQuoter_QuotesEveryComponentAndRejectsNulls()
    {
        Assert.Equal("\"a.b\".\"c\"\"d\".\"select\"", PostgreSqlIdentifierQuoter.Qualified("a.b", "c\"d", "select"));
        Assert.Throws<ArgumentException>(() => PostgreSqlIdentifierQuoter.Quote("a\0b"));
    }

    [Fact]
    public void SecuritySql_ParameterisesSensitiveValuesAndRejectsInjectedSignatures()
    {
        var command = RoleSqlBuilder.Create(new("role\";drop", Password: "secret", PasswordExpiry: DateTimeOffset.UtcNow));
        Assert.Contains("\"role\"\";drop\"", command.Sql);
        Assert.DoesNotContain("secret", command.Sql);
        Assert.Contains("@password_expiry", command.Sql);
        Assert.Throws<ArgumentException>(() => PrivilegeSqlBuilder.Grant("user",
            new(PrivilegeTargetKind.Function, "f", "public", "integer); DROP TABLE x;--"),
            [SecurityPrivilege.Execute]));
    }

    private sealed class MemoryCredentialStore : IProtectedCredentialStore
    {
        private readonly Dictionary<string, char[]> _values = new(StringComparer.Ordinal);
        public ValueTask StoreAsync(string reference, ReadOnlyMemory<char> secret, CancellationToken cancellationToken = default)
        { _values[reference] = secret.ToArray(); return ValueTask.CompletedTask; }
        public ValueTask<char[]?> RetrieveAsync(string reference, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_values.TryGetValue(reference, out var value) ? value.ToArray() : null);
        public ValueTask DeleteAsync(string reference, CancellationToken cancellationToken = default)
        { _values.Remove(reference); return ValueTask.CompletedTask; }
    }
}
