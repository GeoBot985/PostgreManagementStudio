using System.Windows;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Desktop;

public sealed class WpfUserConfirmationService : IUserConfirmationService
{
    public bool Confirm(DestructiveOperationRequest request)
    {
        var message = $"Target: {request.Target}\n\n{request.Consequence}";
        if (!string.IsNullOrWhiteSpace(request.RecoveryGuidance))
            message += $"\n\nRecovery: {request.RecoveryGuidance}";
        return MessageBox.Show(message, request.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}
