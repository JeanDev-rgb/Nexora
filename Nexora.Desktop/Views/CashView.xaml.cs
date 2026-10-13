using System.Windows;
using System.Windows.Controls;
using Nexora.Desktop.ViewModels;

namespace Nexora.Desktop.Views;

public partial class CashView : UserControl
{
    public CashView() => InitializeComponent();

    private void ConfirmPayment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CashEntryViewModel entry } button)
            return;

        var result = MessageBox.Show(
            $"¿Confirmas el cobro de {entry.Amount:C2} para {entry.ComputerName}?",
            "Confirmar cobro",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes && DataContext is CashViewModel viewModel)
            viewModel.RegisterPaymentCommand.Execute(entry);
    }
}
