using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Core.Interfaces;
using Nexora.Desktop.ViewModels;
using Nexora.Desktop.Services;
using Nexora.Infrastructure.Persistence;
using Nexora.Infrastructure.Services;

namespace Nexora.Desktop;

public partial class App : Application
{
    private ServiceProvider? _services;
    public ThemeService ThemeService { get; } = new();
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.Load();
        ThemeService.Apply(ThemeService.CurrentTheme);
        var collection = new ServiceCollection();
        collection.AddDbContextFactory<NexoraDbContext>(options =>
            options.UseSqlite($"Data Source={NexoraDatabase.DefaultPath}"));
        collection.AddSingleton<INexoraService, NexoraService>();
        collection.AddSingleton(ThemeService);
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
