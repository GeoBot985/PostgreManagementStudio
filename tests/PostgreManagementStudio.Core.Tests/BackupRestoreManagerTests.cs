using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class BackupRestoreManagerTests
{
    [Fact]
    public void SupportsTarAndRequiresExplicitOverwrite()
    {
        var request = BackupCommandBuilder.Build(new(new("localhost", 5432, "db", "user"), "archive.tar", BackupFormat.Tar), new("dump", "restore", "psql"));
        Assert.Contains("tar", request.Arguments);
        var path = Path.GetTempFileName();
        try
        {
            Assert.Throws<IOException>(() =>
                BackupSafetyValidator.ValidateDestination(path, BackupFormat.Custom));
            Assert.Equal(Path.GetFullPath(path),
                BackupSafetyValidator.ValidateDestination(path, BackupFormat.Custom, true));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task TemporaryCredentialFileIsUniqueAndCanBeCleaned()
    {
        var service = new TemporaryCredentialService(); var result = await service.CreateAsync(new("localhost", 5432, "db", "user", "secret")); try { Assert.True(File.Exists(result.Path)); Assert.DoesNotContain("secret", result.Environment.Values); Assert.Contains("PGPASSFILE", result.Environment.Keys); } finally { service.Delete(result.Path); Assert.False(File.Exists(result.Path)); }
    }

    [Fact]
    public void VerifiesBackupOutputAndParsesToolVersions()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, "PGDMP"u8.ToArray());
            BackupSafetyValidator.VerifyOutput(path, BackupFormat.Custom);
            Assert.Equal(18, PostgreSqlToolVersionParser.Major("pg_restore (PostgreSQL) 18.4"));
        }
        finally { File.Delete(path); }
    }
}
