namespace PostgreManagementStudio.Application;

public enum DestructiveOperationKind
{
    Restore,
    Maintenance,
    ActualExecutionPlan,
    SessionTermination,
    SchemaChange,
    DataReplacement,
    SecurityChange,
}

public sealed record DestructiveOperationRequest(
    DestructiveOperationKind Kind,
    string Title,
    string Target,
    string Consequence,
    string? RecoveryGuidance = null);

public interface IUserConfirmationService
{
    bool Confirm(DestructiveOperationRequest request);
}

public sealed class DestructiveOperationGuard(IUserConfirmationService confirmation)
{
    public bool Confirm(DestructiveOperationRequest request)
    {
        Validate(request);
        return confirmation.Confirm(request);
    }

    public async Task<bool> ExecuteAsync(
        DestructiveOperationRequest request,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!Confirm(request)) return false;
        await operation(cancellationToken);
        return true;
    }

    private static void Validate(DestructiveOperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Target);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Consequence);
    }
}
