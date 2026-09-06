using System.Windows;
using GarlicSaveMgr;

namespace GarlicSaveMgr.Infrastructure;

public static class DialogService
{
    public static void ShowInfo(Window? owner, string message, string title = "Garlic SaveMgr") => Show(owner, message, title, DialogSeverity.Information, false);
    public static void ShowWarning(Window? owner, string message, string title = "Garlic SaveMgr") => Show(owner, message, title, DialogSeverity.Warning, false);
    public static void ShowError(Window? owner, string message, string title = "Garlic SaveMgr") => Show(owner, message, title, DialogSeverity.Error, false);

    public static bool Confirm(Window? owner, string message, string title = "Confirmar operación", DialogSeverity severity = DialogSeverity.Question)
        => Show(owner, message, title, severity, true);

    private static bool Show(Window? owner, string message, string title, DialogSeverity severity, bool showCancel)
    {
        var dialog = new ThemedDialogWindow(title, message, severity, showCancel)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true;
    }
}
