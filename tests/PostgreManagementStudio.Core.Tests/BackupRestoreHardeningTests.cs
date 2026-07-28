using System.Text;
using System.Text.Json;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class BackupRestoreHardeningTests
{
    [Theory]
    [InlineData("pg_dump (PostgreSQL) 18.4", 18)]
    [InlineData("pg_restore (PostgreSQL) 16beta2", 16)]
    [InlineData("psql (PostgreSQL) 15.10", 15)]
    [InlineData("", null)]
    [InlineData("unexpected output", null)]
    [InlineData("pg_dump PostgreSQL x.y", null)]
    public void ToolVersionsAreParsedDefensively(string text, int? expected) =>
        Assert.Equal(expected, PostgreSqlToolVersionParser.Major(text));

    [Fact]
    public void CompatibilityRulesRejectOlderDumpAndWarnForUnknownOrNewerVersions()
    {
        Assert.False(PostgreSqlToolCompatibility.ForBackup(15, 16).Supported);
        Assert.True(PostgreSqlToolCompatibility.ForBackup(16, 16).Supported);
        Assert.NotEmpty(PostgreSqlToolCompatibility.ForBackup(18, 16).Warnings);
        Assert.NotEmpty(PostgreSqlToolCompatibility.ForBackup(16, null).Warnings);
        Assert.NotEmpty(PostgreSqlToolCompatibility.ForRestore(16, 15).Warnings);
    }

    [Fact]
    public void StructuredArgumentsPreserveSpecialValuesAndNeverPreviewPassword()
    {
        var root = NewDirectory("space & unicode-数据");
        try
        {
            var destination = Path.Combine(root, "leading - (archive) ' \" &.backup");
            var password = "unit-secret'\"&";
            var options = new BackupOptions(
                new("local host", 5432, "odd-db", "user name", password),
                destination, BackupFormat.Custom);
            var request = BackupCommandBuilder.Build(options,
                new("pg_dump.exe", "pg_restore.exe", "psql.exe"));
            Assert.Contains(destination, request.Arguments);
            Assert.DoesNotContain(password, request.Arguments);
            Assert.Equal(password, request.Environment!["PGPASSWORD"]);
            Assert.DoesNotContain(password, BackupCommandBuilder.Preview(request));
            Assert.DoesNotContain("cmd.exe", request.FileName, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PlansSnapshotMutableInputsAndAreSafeToRenderOrSerialize()
    {
        var root = NewDirectory();
        try
        {
            var password = "never-log-me";
            var options = new BackupOptions(
                new("localhost", 5432, "db", "user", password),
                Path.Combine(root, "archive.backup"));
            var plan = BackupOperationPlanFactory.CreateBackup("profile", "server", options,
                Tools(), 16);
            Assert.NotEqual(plan.Destination, plan.TemporaryDestination);
            Assert.True(Path.IsPathFullyQualified(plan.Destination));
            Assert.DoesNotContain(password, plan.ToString());
            Assert.DoesNotContain(password, JsonSerializer.Serialize(plan));
            Assert.Equal(options.Connection.Database, plan.Connection.Database);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DetectsSupportedFormatsWithoutTrustingExtensionsOrLoadingWholeFile()
    {
        var root = NewDirectory();
        try
        {
            var custom = Path.Combine(root, "custom.sql");
            File.WriteAllBytes(custom, "PGDMPrest"u8.ToArray());
            var plain = Path.Combine(root, "plain.backup");
            File.WriteAllText(plain, "-- PostgreSQL database dump\nCREATE TABLE x(id int);");
            var tar = Path.Combine(root, "archive.bin");
            var tarBytes = new byte[512];
            "ustar"u8.CopyTo(tarBytes.AsSpan(257));
            File.WriteAllBytes(tar, tarBytes);
            var directory = Path.Combine(root, "directory");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "toc.dat"), "toc");
            var corrupt = Path.Combine(root, "corrupt.backup");
            File.WriteAllBytes(corrupt, [1, 2, 3, 4]);

            Assert.Equal(BackupFormat.Custom, BackupInspectionService.DetectFormat(custom));
            Assert.Equal(BackupFormat.PlainSql, BackupInspectionService.DetectFormat(plain));
            Assert.Equal(BackupFormat.Tar, BackupInspectionService.DetectFormat(tar));
            Assert.Equal(BackupFormat.Directory, BackupInspectionService.DetectFormat(directory));
            Assert.Null(BackupInspectionService.DetectFormat(corrupt));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LifecycleRejectsInvalidTransitionsAndCancellationIsIdempotent()
    {
        await using var controller = new BackupRestoreOperationController();
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.RunAsync(id,
            (state, _) =>
            {
                state(BackupRestoreOperationState.Completed);
                return Task.FromResult(Result(id, BackupRestoreOperationState.Completed));
            }));
        controller.Cancel();
        controller.Cancel();
        Assert.False(controller.CanCancel);
    }

    [Fact]
    public async Task DisposedControllerCannotBeRestarted()
    {
        var controller = new BackupRestoreOperationController();
        await controller.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            controller.RunAsync(Guid.NewGuid(), (_, _) =>
                Task.FromResult(Result(Guid.NewGuid(), BackupRestoreOperationState.Completed))));
        Assert.Equal(BackupRestoreOperationState.Disposed, controller.State);
    }

    [Fact]
    public void ConfirmationIsBoundToExactTargetAndOperation()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "source.sql");
            File.WriteAllText(source, "-- dump");
            var inspection = new BackupInspectionResult(BackupFormat.PlainSql, null, null,
                new FileInfo(source).Length, 0, [], true, null);
            var plan = BackupOperationPlanFactory.CreateRestore("profile", "server-a",
                new(new("host", 5432, "db", "user"), source, BackupFormat.PlainSql, Clean: true),
                inspection, Tools(), 16);
            var token = RestoreConfirmation.Create(plan);
            Assert.True(RestoreConfirmation.Matches(plan, token));
            Assert.False(RestoreConfirmation.Matches(plan with
            {
                Connection = plan.Connection with { Database = "other" },
            }, token));
            Assert.Contains("host:5432", RestoreConfirmation.Summary(plan));
            Assert.Contains("db", RestoreConfirmation.Summary(plan));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CreateDatabaseRestoreRequiresMatchingArchiveIdentityAndUsesMaintenanceDatabase()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "source.backup");
            File.WriteAllBytes(source, "PGDMParchive"u8.ToArray());
            var options = new RestoreOptions(
                new("host", 5432, "expected_db", "user"), source,
                BackupFormat.Custom, CreateDatabase: true);
            var mismatch = new BackupInspectionResult(BackupFormat.Custom, "16", "other_db",
                new FileInfo(source).Length, 1, [], true, null);
            Assert.Throws<BackupRestoreException>(() =>
                BackupOperationPlanFactory.CreateRestore("profile", "server", options,
                    mismatch, Tools(), 16));

            var matching = mismatch with { SourceDatabase = "expected_db" };
            var plan = BackupOperationPlanFactory.CreateRestore("profile", "server", options,
                matching, Tools(), 16);
            var request = RestoreCommandBuilder.Build(plan.Options,
                new("pg_dump.exe", "pg_restore.exe", "psql.exe"));
            var databaseIndex = request.Arguments.IndexOf("--dbname");
            Assert.Equal("postgres", request.Arguments[databaseIndex + 1]);
            Assert.True(plan.IsDestructive);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ConflictingResourcesAreRejectedAndReleased()
    {
        var locks = new BackupOperationLockManager(2);
        var firstId = Guid.NewGuid();
        await using (await locks.AcquireAsync(firstId, "server/db"))
        {
            var error = await Assert.ThrowsAsync<BackupRestoreException>(() =>
                locks.AcquireAsync(Guid.NewGuid(), "server/db"));
            Assert.Equal(BackupRestoreFailureCategory.RestoreConflict, error.Category);
            await using var independent = await locks.AcquireAsync(Guid.NewGuid(), "server/other");
        }
        await using var reused = await locks.AcquireAsync(Guid.NewGuid(), "server/db");
    }

    [Fact]
    public async Task BackupPublishesOnlyVerifiedOutputAndPreservesExistingDestinationOnFailure()
    {
        var root = NewDirectory();
        try
        {
            var destination = Path.Combine(root, "safe.backup");
            File.WriteAllText(destination, "known-good");
            var options = new BackupOptions(
                new("localhost", 5432, "db", "user", "secret"), destination);
            var plan = BackupOperationPlanFactory.CreateBackup("profile", "server", options,
                Tools(), 16, allowOverwrite: true);
            var runner = new FakeRunner(request =>
            {
                var output = request.Arguments[request.Arguments.IndexOf("--file") + 1];
                File.WriteAllBytes(output, "not-an-archive"u8.ToArray());
                return new(0, [], false);
            });
            var service = new BackupRestoreOperationService(runner, new SuccessfulValidator(),
                new BackupInspectionService(runner), new BackupOperationLockManager());
            await using var controller = new BackupRestoreOperationController();
            var result = await service.ExecuteBackupAsync(plan, controller);

            Assert.Equal(BackupRestoreOperationState.Failed, result!.State);
            Assert.Equal("known-good", File.ReadAllText(destination));
            Assert.False(File.Exists(plan.TemporaryDestination));
            Assert.False(result.PartialOutputRemains);
            Assert.DoesNotContain("secret", string.Join('\n', result.Output.Select(x => x.Line)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task BackupSuccessIsVerifiedAndAtomicallyCommitted()
    {
        var root = NewDirectory();
        try
        {
            var destination = Path.Combine(root, "safe.backup");
            var plan = BackupOperationPlanFactory.CreateBackup("profile", "server",
                new(new("localhost", 5432, "db", "user"), destination), Tools(root), 16);
            var runner = new FakeRunner(request =>
            {
                if (request.Arguments.Contains("--list"))
                    return new(0, [new(false, "TABLE public.sample")], false);
                var output = request.Arguments[request.Arguments.IndexOf("--file") + 1];
                File.WriteAllBytes(output, "PGDMPvalid"u8.ToArray());
                return new(0, [new(true, "pg_dump: warning: sample warning")], false);
            });
            var service = new BackupRestoreOperationService(runner, new SuccessfulValidator(),
                new BackupInspectionService(runner), new BackupOperationLockManager());
            await using var controller = new BackupRestoreOperationController();
            var result = await service.ExecuteBackupAsync(plan, controller);

            Assert.Equal(BackupRestoreOperationState.CompletedWithWarnings, result!.State);
            Assert.True(result.AtomicCommit);
            Assert.True(result.VerificationSucceeded);
            Assert.True(File.Exists(destination));
            Assert.Single(result.Warnings);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("password authentication failed", BackupRestoreFailureCategory.Authentication)]
    [InlineData("archive version 99 is not supported", BackupRestoreFailureCategory.UnsupportedArchiveVersion)]
    [InlineData("permission denied", BackupRestoreFailureCategory.PermissionDenied)]
    [InlineData("input file does not appear to be a valid archive", BackupRestoreFailureCategory.CorruptBackup)]
    [InlineData("database \"gone\" does not exist", BackupRestoreFailureCategory.DatabaseNotFound)]
    public void ExternalErrorsAreClassified(string text, BackupRestoreFailureCategory expected) =>
        Assert.Equal(expected, BackupRestoreErrorClassifier.ClassifyProcess(1, [new(true, text)]));

    [Fact]
    public void WarningsAreGroupedAndSecretsAreRedacted()
    {
        var warnings = BackupWarningClassifier.Warnings(
        [
            new(true, "WARNING: password=secret"),
            new(true, "warning: password=secret"),
            new(true, "ordinary stderr"),
        ]);
        Assert.Single(warnings);
        Assert.DoesNotContain("secret", warnings[0]);
        Assert.DoesNotContain("pw", BackupSecretRedactor.Redact("postgres://u:pw@host/db"));
    }

    [Fact]
    public async Task ProcessOutputIsBoundedAndCancellationTerminatesTheApprovedProcess()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = NewDirectory();
        try
        {
            var executable = Path.Combine(root, "psql.exe");
            File.Copy(Environment.GetEnvironmentVariable("ComSpec")!, executable);
            var runner = new ExternalProcessRunner(10, TimeSpan.FromMilliseconds(100));
            var output = await runner.RunAsync(new(executable,
                ["/d", "/c", "for /L %i in (1,1,100) do @echo line-%i"]));
            Assert.Equal(0, output.ExitCode);
            Assert.True(output.OutputTruncated);
            Assert.Equal(10, output.Output.Count);

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            var cancelled = await runner.RunAsync(new(executable,
                ["/d", "/c", "ping -n 30 127.0.0.1 >nul"]), cancellationToken: cancellation.Token);
            Assert.True(cancelled.Cancelled);
            Assert.Equal(-1, cancelled.ExitCode);
            Assert.NotNull(cancelled.StartedAt);
            Assert.NotNull(cancelled.CompletedAt);
        }
        finally { Directory.Delete(root, true); }
    }

    private static ValidatedPostgreSqlTools Tools(string? directory = null)
    {
        var dump = directory is null ? "pg_dump.exe" : Path.Combine(directory, "pg_dump.exe");
        var restore = directory is null ? "pg_restore.exe" : Path.Combine(directory, "pg_restore.exe");
        var psql = directory is null ? "psql.exe" : Path.Combine(directory, "psql.exe");
        if (directory is not null)
        {
            File.WriteAllBytes(dump, []);
            File.WriteAllBytes(restore, []);
            File.WriteAllBytes(psql, []);
        }
        return new(new(dump, restore, psql),
            new("pg_dump", dump, "pg_dump (PostgreSQL) 16", 16),
            new("pg_restore", restore, "pg_restore (PostgreSQL) 16", 16),
            new("psql", psql, "psql (PostgreSQL) 16", 16));
    }

    private static BackupRestoreExecutionResult Result(
        Guid id,
        BackupRestoreOperationState state) => new(
        id, BackupRestoreOperationType.Backup, state, null, "", 0, [], [], 0,
        false, false, false, true, true, false, false,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static string NewDirectory(string suffix = "")
    {
        var path = Path.Combine(Path.GetTempPath(), $"pms-s39-{Guid.NewGuid():N}-{suffix}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class SuccessfulValidator : IBackupRestoreConnectionValidator
    {
        public Task<BackupRestoreValidationResult> ValidateAsync(
            DatabaseConnection connection,
            bool databaseMustExist,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BackupRestoreValidationResult(true, 16, "ok"));
    }

    private sealed class FakeRunner(
        Func<ProcessExecutionRequest, ProcessExecutionResult> execute) : IExternalProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            IProgress<ProcessOutputEntry>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = execute(request);
            foreach (var entry in result.Output) progress?.Report(entry);
            return Task.FromResult(result);
        }
    }
}

internal static class ArgumentListTestExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
            if (string.Equals(values[index], value, StringComparison.Ordinal)) return index;
        return -1;
    }
}
