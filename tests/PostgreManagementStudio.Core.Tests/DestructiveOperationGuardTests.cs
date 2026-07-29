using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Core.Tests;

public sealed class DestructiveOperationGuardTests
{
    [Fact]
    [Trait("Category", "MutationSample")]
    [Trait("Priority", "P0")]
    public async Task DestructiveOperation_DeniedConfirmation_NeverExecutes()
    {
        var confirmation = new RecordingConfirmation(false);
        var guard = new DestructiveOperationGuard(confirmation);
        var executed = false;
        var request = new DestructiveOperationRequest(DestructiveOperationKind.Restore, "Confirm restore", "regression", "Existing objects may be replaced.", "Restore the pre-operation backup.", "localhost:5432", "regression");
        var accepted = await guard.ExecuteAsync(request, _ => { executed = true; return Task.CompletedTask; });
        Assert.False(accepted);
        Assert.False(executed);
        Assert.Same(request, confirmation.Request);
    }

    [Fact]
    [Trait("Category", "Component")]
    [Trait("Priority", "P0")]
    public void DestructiveOperation_MissingTargetOrConsequence_IsRejectedBeforePrompt()
    {
        var confirmation = new RecordingConfirmation(true);
        var guard = new DestructiveOperationGuard(confirmation);
        Assert.Throws<ArgumentException>(() => guard.Confirm(new(DestructiveOperationKind.Restore, "Restore", "", "changes", Server: "host", Database: "db")));
        Assert.Throws<ArgumentException>(() => guard.Confirm(new(DestructiveOperationKind.Restore, "Restore", "db", "", Server: "host", Database: "db")));
        Assert.Null(confirmation.Request);
    }

    [Fact]
    public void ProductionOperation_RequiresTypedExactDatabaseConfirmation()
    {
        var confirmation = new RecordingConfirmation(true);
        var guard = new DestructiveOperationGuard(confirmation);
        Assert.True(guard.Confirm(new(DestructiveOperationKind.SchemaChange, "Drop table", "orders",
            "The table and its data will be removed.", Server: "prod:5432", Database: "sales",
            ObjectName: "public.orders", EnvironmentClassification: "Production")));
        Assert.Equal("sales", confirmation.Request!.RequiredConfirmationPhrase);
        Assert.Equal("prod:5432 / sales / public.orders", confirmation.Request.ExactTarget);
    }

    [Fact]
    public void UncertainSession_NeverPromptsOrExecutes()
    {
        var confirmation = new RecordingConfirmation(true);
        var guard = new DestructiveOperationGuard(confirmation);
        Assert.False(guard.Confirm(new(DestructiveOperationKind.SchemaChange, "Drop", "table", "Data loss",
            Server: "host", Database: "db", SessionIdentityCertain: false)));
        Assert.Null(confirmation.Request);
    }

    private sealed class RecordingConfirmation(bool response) : IUserConfirmationService
    {
        public DestructiveOperationRequest? Request { get; private set; }
        public bool Confirm(DestructiveOperationRequest request) { Request = request; return response; }
    }
}
