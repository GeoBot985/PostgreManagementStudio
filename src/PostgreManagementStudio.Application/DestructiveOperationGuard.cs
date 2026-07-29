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
    string? RecoveryGuidance = null,
    string? Server = null,
    string? Database = null,
    string? ObjectName = null,
    string? EnvironmentClassification = null,
    bool SessionIdentityCertain = true,
    string? RequiredConfirmationPhrase = null)
{
    public string ExactTarget => string.Join(" / ", new[] { Server, Database, ObjectName ?? Target }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public bool IsProduction => string.Equals(EnvironmentClassification, "Production", StringComparison.OrdinalIgnoreCase);
}

public interface IUserConfirmationService
{
    bool Confirm(DestructiveOperationRequest request);
}

public sealed class DestructiveOperationGuard(IUserConfirmationService confirmation)
{
    private readonly HashSet<string> _operationsInProgress = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public bool Confirm(DestructiveOperationRequest request)
    {
        Validate(request);
        if (!request.SessionIdentityCertain) return false;
        if (request.IsProduction && string.IsNullOrWhiteSpace(request.RequiredConfirmationPhrase))
            request = request with { RequiredConfirmationPhrase = request.Database ?? request.ObjectName ?? request.Target };
        return confirmation.Confirm(request);
    }

    public async Task<bool> ExecuteAsync(
        DestructiveOperationRequest request,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var key = $"{request.Kind}:{request.ExactTarget}";
        lock (_gate)
            if (!_operationsInProgress.Add(key)) return false;
        try
        {
            if (!Confirm(request)) return false;
            await operation(cancellationToken);
            return true;
        }
        finally { lock (_gate) _operationsInProgress.Remove(key); }
    }

    private static void Validate(DestructiveOperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Target);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Consequence);
        if (string.IsNullOrWhiteSpace(request.Server)) throw new ArgumentException("The exact server is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Database)) throw new ArgumentException("The exact database is required.", nameof(request));
    }
}
