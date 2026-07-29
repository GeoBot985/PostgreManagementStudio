using PostgreManagementStudio.Core;

namespace PostgreManagementStudio.Desktop;

public static class DesktopErrorPresentation
{
    public static string Failure(string operation, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is OperationCanceledException) return $"{operation} cancelled.";

        var message = SensitiveDataRedactor.Redact(exception.Message);
        return string.IsNullOrWhiteSpace(message)
            ? $"{operation} failed."
            : $"{operation} failed: {UntrustedText.ForDisplay(message, 2_048)}";
    }
}
