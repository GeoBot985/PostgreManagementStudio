using System.Windows;
using System.Windows.Controls;
using PostgreManagementStudio.Application;

namespace PostgreManagementStudio.Desktop;

public sealed class WpfUserConfirmationService : IUserConfirmationService
{
    public bool Confirm(DestructiveOperationRequest request)
    {
        var message = $"Exact target: {request.ExactTarget}\nEnvironment: {request.EnvironmentClassification ?? "Unclassified"}\n\n{request.Consequence}";
        if (!string.IsNullOrWhiteSpace(request.RecoveryGuidance))
            message += $"\n\nRecovery: {request.RecoveryGuidance}";
        if (!string.IsNullOrWhiteSpace(request.RequiredConfirmationPhrase))
            return TypedConfirmationWindow.Show(request.Title, message, request.RequiredConfirmationPhrase);
        return MessageBox.Show(message, request.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}

file sealed class TypedConfirmationWindow : Window
{
    private readonly TextBox _entry = new();
    private readonly Button _confirm = new() { Content = "Confirm", Width = 90, IsDefault = true, IsEnabled = false };

    private TypedConfirmationWindow(string title, string message, string phrase)
    {
        Title = title;
        Width = 560;
        Height = 300;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"\nType exactly: {phrase}", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_entry);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(_confirm);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        _entry.TextChanged += (_, _) => _confirm.IsEnabled = string.Equals(_entry.Text, phrase, StringComparison.Ordinal);
        _confirm.Click += (_, _) => DialogResult = true;
    }

    public static bool Show(string title, string message, string phrase)
        => new TypedConfirmationWindow(title, message, phrase) { Owner = System.Windows.Application.Current?.MainWindow }.ShowDialog() == true;
}
