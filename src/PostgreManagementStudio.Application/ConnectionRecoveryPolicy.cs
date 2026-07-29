namespace PostgreManagementStudio.Application;

public enum TransactionFailureWindow
{
    BeforeCommandTransmission,
    DuringCommandExecution,
    AfterExecutionBeforeAcknowledgement,
    DuringCommit,
    DuringRollback,
}

public enum QueryTransactionRecoveryState
{
    None,
    ServerRolledBackUncommittedWork,
    OutcomeUnknown,
}

public sealed record TransactionRecoveryAssessment(
    QueryTransactionRecoveryState State,
    string Message,
    bool MustClearLocalTransaction,
    bool MayRetry);

public static class TransactionRecoveryPolicy
{
    public static TransactionRecoveryAssessment Assess(
        bool transactionWasActive,
        TransactionFailureWindow failureWindow)
    {
        if (!transactionWasActive)
            return new(QueryTransactionRecoveryState.None,
                "No active transaction was associated with the failed backend.", true, false);
        if (failureWindow == TransactionFailureWindow.DuringCommit)
            return new(QueryTransactionRecoveryState.OutcomeUnknown,
                "The connection was lost while COMMIT was in progress. The final transaction outcome is unknown and must be verified against PostgreSQL.",
                true, false);
        return new(QueryTransactionRecoveryState.ServerRolledBackUncommittedWork,
            failureWindow == TransactionFailureWindow.DuringRollback
                ? "The connection closed while ROLLBACK was in progress; PostgreSQL discards uncommitted work when the backend ends."
                : "The backend connection ended, so PostgreSQL rolled back its uncommitted transaction work.",
            true, false);
    }
}
