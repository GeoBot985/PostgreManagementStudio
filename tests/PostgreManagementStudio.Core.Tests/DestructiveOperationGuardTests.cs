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
        var request = new DestructiveOperationRequest(DestructiveOperationKind.Restore, "Confirm restore", "regression", "Existing objects may be replaced.", "Restore the pre-operation backup.");
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
        Assert.Throws<ArgumentException>(() => guard.Confirm(new(DestructiveOperationKind.Restore, "Restore", "", "changes")));
        Assert.Throws<ArgumentException>(() => guard.Confirm(new(DestructiveOperationKind.Restore, "Restore", "db", "")));
        Assert.Null(confirmation.Request);
    }

    private sealed class RecordingConfirmation(bool response) : IUserConfirmationService
    {
        public DestructiveOperationRequest? Request { get; private set; }
        public bool Confirm(DestructiveOperationRequest request) { Request = request; return response; }
    }
}
