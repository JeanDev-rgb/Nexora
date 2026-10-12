using System.Windows;
using System.Windows.Threading;
using Nexora.Desktop.ViewModels;

namespace Nexora.Desktop;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => viewModel.RefreshActiveDurations();
        _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer.Stop();
    }
}
