using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Core.Interfaces;
using Nexora.Desktop.ViewModels;
using Nexora.Infrastructure.Persistence;
using Nexora.Infrastructure.Services;

namespace Nexora.Desktop;

public partial class App : Application
{
    private ServiceProvider? _services;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var collection = new ServiceCollection();
        collection.AddDbContextFactory<NexoraDbContext>(options =>
            options.UseSqlite($"Data Source={NexoraDatabase.DefaultPath}"));
        collection.AddSingleton<INexoraService, NexoraService>();
        collection.AddSingleton<MainViewModel>();
        _services = collection.BuildServiceProvider();
        new MainWindow(_services.GetRequiredService<MainViewModel>()).Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
