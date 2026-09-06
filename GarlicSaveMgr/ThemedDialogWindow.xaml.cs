using System.Windows;
using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr;

public enum DialogSeverity
{
    Information,
    Question,
    Warning,
    Error
}

public partial class ThemedDialogWindow : Window
{
    public ThemedDialogWindow(string title, string message, DialogSeverity severity, bool showCancel)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        ApplySeverity(severity);
        Loaded += (_, _) => EnsureFitsWorkArea();
    }

    private void ApplySeverity(DialogSeverity severity)
    {
        var colorKey = severity switch
        {
            DialogSeverity.Error => "Danger",
            DialogSeverity.Warning => "Warning",
            DialogSeverity.Question => "Accent",
            _ => "Accent"
        };
        var softKey = severity switch
        {
            DialogSeverity.Error => "DangerSoft",
            DialogSeverity.Warning => "WarningSoft",
            _ => "AccentSoft"
        };
        SeverityBar.SetResourceReference(BackgroundProperty, colorKey);
        IconCircle.SetResourceReference(BackgroundProperty, softKey);
        IconText.SetResourceReference(ForegroundProperty, colorKey);
        IconText.Text = severity switch
        {
            DialogSeverity.Error => "!",
            DialogSeverity.Warning => "!",
            DialogSeverity.Question => "?",
            _ => "i"
        };
    }

    private void EnsureFitsWorkArea()
    {
        var maxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40);
        if (ActualHeight > maxHeight)
        {
            Height = maxHeight;
            // The message itself remains readable because it wraps and the dialog body is scroll-free only for normal-sized messages.
        }
        Left = Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - ActualWidth) / 2);
        Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - ActualHeight) / 2);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
