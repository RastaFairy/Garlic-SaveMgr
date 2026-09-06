using System.Windows;
using GarlicSaveMgr.Models;

namespace GarlicSaveMgr;

public partial class DeleteSafetyWindow : Window
{
    private DeleteSafetyWindow(string title, string summary, IReadOnlyList<ConfirmationWindow.ConfirmationItem> items)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        SummaryText.Text = summary;
        ItemsList.ItemsSource = items;
    }

    public static PcBackupDeleteMode? ShowPc(Window owner, string title, string summary, IReadOnlyList<ConfirmationWindow.ConfirmationItem> items, bool allowMoveToTrash = true)
    {
        var dlg = new DeleteSafetyWindow(title, summary, items) { Owner = owner };
        dlg.PermanentButtonText.Text = "Eliminar definitivamente";
        dlg.InternalButtonText.Text = allowMoveToTrash ? "Mover a Papelera" : "Papelera no disponible";
        dlg.InternalBackupDescription.Text = allowMoveToTrash
            ? "Retira el backup activo y conserva la copia en pc_trash para poder restaurarla."
            : "La Papelera PC no está cargada. Esta opción requiere Modules/Trash.xaml.";
        dlg.InternalButton.IsEnabled = allowMoveToTrash;
        dlg.ShowDialog();
        return dlg.PcChoice;
    }

    public static ConsoleDeleteMode? Show(Window owner, string title, string summary, IReadOnlyList<ConfirmationWindow.ConfirmationItem> items)
    {
        var dlg = new DeleteSafetyWindow(title, summary, items) { Owner = owner };
        dlg.ShowDialog();
        return dlg.Choice;
    }

    private ConsoleDeleteMode? Choice { get; set; }
    private PcBackupDeleteMode? PcChoice { get; set; }

    private void Permanent_Click(object sender, RoutedEventArgs e)
    {
        Choice = ConsoleDeleteMode.Permanent;
        PcChoice = PcBackupDeleteMode.Permanent;
        DialogResult = true;
    }

    private void InternalBackup_Click(object sender, RoutedEventArgs e)
    {
        Choice = ConsoleDeleteMode.InternalBackup;
        PcChoice = PcBackupDeleteMode.MoveToTrash;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = null;
        DialogResult = false;
    }
}
