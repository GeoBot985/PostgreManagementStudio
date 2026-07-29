using System.Windows.Input;

namespace PostgreManagementStudio.Desktop;

public static class ShellCommands
{
    private static RoutedUICommand Create(string text, string name, params InputGesture[] gestures) =>
        new(text, name, typeof(ShellCommands), new InputGestureCollection(gestures));

    public static RoutedUICommand NewQuery { get; } = Create("New Query", nameof(NewQuery), new KeyGesture(Key.N, ModifierKeys.Control));
    public static RoutedUICommand Connect { get; } = Create("Connect", nameof(Connect));
    public static RoutedUICommand Reconnect { get; } = Create("Reconnect", nameof(Reconnect));
    public static RoutedUICommand Disconnect { get; } = Create("Disconnect", nameof(Disconnect));
    public static RoutedUICommand ChangeConnection { get; } = Create("Change Connection", nameof(ChangeConnection));
    public static RoutedUICommand OpenFile { get; } = Create("Open File", nameof(OpenFile), new KeyGesture(Key.O, ModifierKeys.Control));
    public static RoutedUICommand Save { get; } = Create("Save", nameof(Save), new KeyGesture(Key.S, ModifierKeys.Control));
    public static RoutedUICommand SaveAs { get; } = Create("Save As", nameof(SaveAs), new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
    public static RoutedUICommand CloseDocument { get; } = Create("Close Query", nameof(CloseDocument), new KeyGesture(Key.W, ModifierKeys.Control));
    public static RoutedUICommand CloseOtherDocuments { get; } = Create("Close Other Queries", nameof(CloseOtherDocuments));
    public static RoutedUICommand CloseAllDocuments { get; } = Create("Close All Queries", nameof(CloseAllDocuments), new KeyGesture(Key.W, ModifierKeys.Control | ModifierKeys.Shift));
    public static RoutedUICommand NextDocument { get; } = Create("Next Query", nameof(NextDocument), new KeyGesture(Key.Tab, ModifierKeys.Control));
    public static RoutedUICommand PreviousDocument { get; } = Create("Previous Query", nameof(PreviousDocument), new KeyGesture(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift));
    public static RoutedUICommand Execute { get; } = Create("Execute", nameof(Execute), new KeyGesture(Key.F5), new KeyGesture(Key.Enter, ModifierKeys.Control));
    public static RoutedUICommand Cancel { get; } = Create("Cancel Executing Query", nameof(Cancel), new KeyGesture(Key.Escape));
    public static RoutedUICommand EstimatedPlan { get; } = Create("Display Estimated Execution Plan", nameof(EstimatedPlan), new KeyGesture(Key.L, ModifierKeys.Control));
    public static RoutedUICommand ToggleActualPlan { get; } = Create("Include Actual Execution Plan", nameof(ToggleActualPlan));
    public static RoutedUICommand RefreshObjectExplorer { get; } = Create("Refresh Object Explorer", nameof(RefreshObjectExplorer));
    public static RoutedUICommand Find { get; } = Create("Find", nameof(Find), new KeyGesture(Key.F, ModifierKeys.Control));
    public static RoutedUICommand FindNext { get; } = Create("Find Next", nameof(FindNext), new KeyGesture(Key.F3));
    public static RoutedUICommand Replace { get; } = Create("Replace", nameof(Replace), new KeyGesture(Key.H, ModifierKeys.Control));
    public static RoutedUICommand GoToLine { get; } = Create("Go To Line", nameof(GoToLine), new KeyGesture(Key.G, ModifierKeys.Control));
    public static RoutedUICommand CopyResults { get; } = Create("Copy Results", nameof(CopyResults));
    public static RoutedUICommand CopyResultsWithHeaders { get; } = Create("Copy Results with Headers", nameof(CopyResultsWithHeaders));
    public static RoutedUICommand ExportResults { get; } = Create("Export Results", nameof(ExportResults));
    public static RoutedUICommand FindInResults { get; } = Create("Find in Results", nameof(FindInResults));
    public static RoutedUICommand ClearResults { get; } = Create("Clear Result View", nameof(ClearResults));
    public static RoutedUICommand SearchObjects { get; } = Create("Search Objects", nameof(SearchObjects), new KeyGesture(Key.F, ModifierKeys.Control | ModifierKeys.Shift));
    public static RoutedUICommand ImportData { get; } = Create("Import Data", nameof(ImportData));
    public static RoutedUICommand Backup { get; } = Create("Backup Database", nameof(Backup));
    public static RoutedUICommand Restore { get; } = Create("Restore Database", nameof(Restore));
    public static RoutedUICommand Maintenance { get; } = Create("Maintenance", nameof(Maintenance));
    public static RoutedUICommand Security { get; } = Create("Security Roles", nameof(Security));
    public static RoutedUICommand ShowObjectExplorer { get; } = Create("Object Explorer", nameof(ShowObjectExplorer));
    public static RoutedUICommand ShowResults { get; } = Create("Results", nameof(ShowResults));
    public static RoutedUICommand ShowMessages { get; } = Create("Messages", nameof(ShowMessages));
    public static RoutedUICommand ShowExecutionPlan { get; } = Create("Execution Plan", nameof(ShowExecutionPlan));
    public static RoutedUICommand About { get; } = Create("About PostgreManagementStudio", nameof(About));
}

public enum ShellCommandId
{
    OpenFile, Save, SaveAs, CloseDocument, Execute, Cancel, ChangeConnection,
    EstimatedPlan, ActualPlan, ResultAction, ConnectedTool
}

public readonly record struct ShellCommandState(
    bool HasDocument,
    bool IsConnected,
    bool IsExecuting,
    bool HasResults,
    bool IsDirty)
{
    public bool CanExecute(ShellCommandId command) => command switch
    {
        ShellCommandId.OpenFile or ShellCommandId.CloseDocument => HasDocument && !IsExecuting,
        ShellCommandId.SaveAs => HasDocument && !IsExecuting,
        ShellCommandId.Save => HasDocument && IsDirty && !IsExecuting,
        ShellCommandId.Execute => HasDocument && IsConnected && !IsExecuting,
        ShellCommandId.Cancel => HasDocument && IsExecuting,
        ShellCommandId.ChangeConnection => HasDocument && !IsExecuting,
        ShellCommandId.EstimatedPlan or ShellCommandId.ActualPlan or ShellCommandId.ConnectedTool =>
            HasDocument && IsConnected && !IsExecuting,
        ShellCommandId.ResultAction => HasDocument && HasResults,
        _ => false,
    };
}
