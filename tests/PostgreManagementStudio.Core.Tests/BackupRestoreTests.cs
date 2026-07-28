using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class BackupRestoreTests
{
    private static readonly PostgreSqlTools Tools = new("C:/Postgre Tools/pg_dump.exe", "C:/Postgre Tools/pg_restore.exe", "C:/Postgre Tools/psql.exe");
    private static readonly DatabaseConnection Connection = new("localhost", 5432, "test db", "user name", "secret-password");

    [Fact]
    public void CustomBackupUsesStructuredArgumentsAndHidesPassword()
    {
        var request = BackupCommandBuilder.Build(new(Connection, "C:/exports/my backup.backup", BackupFormat.Custom, DataOnly: true), Tools);
        Assert.Equal(Tools.PgDump, request.FileName); Assert.Contains("--format", request.Arguments); Assert.Contains("custom", request.Arguments); Assert.DoesNotContain("secret-password", BackupCommandBuilder.Preview(request)); Assert.Equal("secret-password", request.Environment!["PGPASSWORD"]);
    }

    [Fact]
    public void PlainAndDirectoryBackupsSelectTheirFormats()
    {
        Assert.Contains("plain", BackupCommandBuilder.Build(new(Connection, "out.sql", BackupFormat.PlainSql), Tools).Arguments);
        Assert.Contains("directory", BackupCommandBuilder.Build(new(Connection, "out-dir", BackupFormat.Directory), Tools).Arguments);
    }

    [Fact]
    public void PlainRestoreUsesPsqlAndCustomUsesPgRestore()
    {
        var plain = RestoreCommandBuilder.Build(new(Connection, Path.GetTempFileName(), BackupFormat.PlainSql), Tools); var custom = RestoreCommandBuilder.Build(new(Connection, Path.GetTempFileName(), BackupFormat.Custom), Tools);
        Assert.Equal(Tools.Psql, plain.FileName); Assert.Equal(Tools.PgRestore, custom.FileName);
    }

    [Fact]
    public void ConflictingOptionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => BackupCommandBuilder.Build(new(Connection, "out.backup", DataOnly: true, SchemaOnly: true), Tools));
        Assert.Throws<ArgumentException>(() => BackupCommandBuilder.Build(new(Connection, "out.sql", BackupFormat.PlainSql, CompressionLevel: 6), Tools));
    }
}
