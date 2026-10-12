using System.Windows;
using System.Windows.Threading;
using Nexora.Desktop.ViewModels;

namespace Nexora.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => viewModel.RefreshActiveDurations();
        timer.Start();
    }
}
