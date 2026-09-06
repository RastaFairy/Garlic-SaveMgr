using System.Windows;
using System.Windows.Controls;

namespace GarlicSaveMgr;

public partial class ConfirmationWindow : Window
{
    public sealed record ConfirmationItem(string Primary, string Secondary);

    private ConfirmationWindow(string title, string summary, IReadOnlyList<ConfirmationItem> items, string actionText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        SummaryText.Text = summary;
        CountText.Text = $"{items.Count} elemento(s) seleccionado(s)";
        ConfirmButtonText(actionText);
        ItemsList.ItemsSource = items;
    }

    public static bool Show(Window owner, string title, string summary, IReadOnlyList<ConfirmationItem> items, string actionText)
    {
        var dlg = new ConfirmationWindow(title, summary, items, actionText) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    private void ConfirmButtonText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // Mantener el texto del botón corto y legible. La explicación completa
        // permanece en el título/resumen de la ventana.
        var compactText = text switch
        {
            "Eliminar y perder definitivamente" => "Eliminar definitivamente",
            "Sobrescribir y continuar" => "Sobrescribir",
            "Eliminar definitivamente" => "Eliminar",
            _ => text
        };

        ConfirmButton.Content = new TextBlock
        {
            Text = compactText,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 120
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
