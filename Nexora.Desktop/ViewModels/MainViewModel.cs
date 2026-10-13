using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexora.Core.Enums;
using Nexora.Core.Interfaces;
using Nexora.Core.Models;
using Nexora.Desktop.Services;

namespace Nexora.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly INexoraService _service;
    private readonly ThemeService _themeService;
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.Local;

    [ObservableProperty] private string newComputerName = string.Empty;
    [ObservableProperty] private string newTariffName = string.Empty;
    [ObservableProperty] private string newTariffPrice = string.Empty;
    [ObservableProperty] private string message = "Listo.";
    [ObservableProperty] private int totalComputers;
    [ObservableProperty] private int activeSessions;
    [ObservableProperty] private decimal todayRevenue;
    [ObservableProperty] private decimal activeEstimatedAmount;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string currentSection = "Dashboard";
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string stationFilter = "Todos";
    [ObservableProperty] private AppearanceTheme selectedTheme;
    [ObservableProperty] private int availableComputers;
    [ObservableProperty] private int outOfServiceComputers;
    [ObservableProperty] private int pendingPayments;
    [ObservableProperty] private string historyComputerFilter = "Todos";
    [ObservableProperty] private string historyPaymentFilter = "Todos";
    [ObservableProperty] private string historyModeFilter = "Todos";
    [ObservableProperty] private string historyStateFilter = "Todos";
    [ObservableProperty] private DateTime? historyFromDate;
    [ObservableProperty] private DateTime? historyToDate;

    public ObservableCollection<ComputerCardViewModel> Computers { get; } = [];
    public ObservableCollection<ComputerCardViewModel> VisibleComputers { get; } = [];
    public ObservableCollection<Tariff> Tariffs { get; } = [];
    public ObservableCollection<SessionSummary> History { get; } = [];
    public ObservableCollection<SessionSummary> VisibleHistory { get; } = [];
    public ObservableCollection<string> HistoryComputerOptions { get; } = ["Todos"];
    public IReadOnlyList<string> HistoryPaymentOptions { get; } = ["Todos", "Pendiente", "Pagada"];
    public IReadOnlyList<string> HistoryModeOptions { get; } = ["Todos", "Tiempo libre", "Tiempo contratado"];
    public IReadOnlyList<string> HistoryStateOptions { get; } = ["Todos", "Activa", "Completada", "Tiempo agotado"];
    public IReadOnlyList<string> StationFilters { get; } = ["Todos", "Disponibles", "En uso", "Fuera de servicio"];
    public IReadOnlyList<AppearanceTheme> ThemeOptions { get; } = Enum.GetValues<AppearanceTheme>();

    public MainViewModel(INexoraService service, ThemeService themeService)
    {
        _service = service;
        _themeService = themeService;
        SelectedTheme = themeService.CurrentTheme;
    }

    public bool IsDashboard => CurrentSection == "Dashboard";
    public bool IsStations => CurrentSection == "Estaciones";
    public bool IsCash => CurrentSection == "Caja";
    public bool IsHistory => CurrentSection == "Historial";
    public bool IsSettings => CurrentSection == "Configuración";

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            await _service.InitializeAsync();
            var computers = await _service.GetComputersAsync();
            var tariffs = await _service.GetTariffsAsync();
            var activeSessions = await _service.GetActiveSessionsAsync();
            var history = await _service.GetRecentSessionsAsync();
            var dashboard = await _service.GetDashboardAsync(DateTime.Now, _timeZone);

            Computers.Clear();
            foreach (var computer in computers)
                Computers.Add(CreateCard(computer, activeSessions, tariffs));
            ApplyFilters();

            Tariffs.Clear();
            foreach (var tariff in tariffs)
                Tariffs.Add(tariff);

            History.Clear();
            foreach (var item in history)
                History.Add(item);
            HistoryComputerOptions.Clear();
            HistoryComputerOptions.Add("Todos");
            foreach (var computerName in history.Select(x => x.ComputerName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                HistoryComputerOptions.Add(computerName);
            ApplyHistoryFilters();

            TotalComputers = dashboard.TotalComputers;
            ActiveSessions = dashboard.ActiveSessions;
            AvailableComputers = dashboard.AvailableComputers;
            OutOfServiceComputers = dashboard.OutOfServiceComputers;
            PendingPayments = dashboard.PendingPayments;
            TodayRevenue = dashboard.TodayRevenue;
            ActiveEstimatedAmount = dashboard.ActiveEstimatedAmount;
            Message = "Datos actualizados.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnCurrentSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(IsStations));
        OnPropertyChanged(nameof(IsCash));
        OnPropertyChanged(nameof(IsHistory));
        OnPropertyChanged(nameof(IsSettings));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnStationFilterChanged(string value) => ApplyFilters();
    partial void OnHistoryComputerFilterChanged(string value) => ApplyHistoryFilters();
    partial void OnHistoryPaymentFilterChanged(string value) => ApplyHistoryFilters();
    partial void OnHistoryModeFilterChanged(string value) => ApplyHistoryFilters();
    partial void OnHistoryStateFilterChanged(string value) => ApplyHistoryFilters();
    partial void OnHistoryFromDateChanged(DateTime? value) => ApplyHistoryFilters();
    partial void OnHistoryToDateChanged(DateTime? value) => ApplyHistoryFilters();

    partial void OnSelectedThemeChanged(AppearanceTheme value) => _themeService.Apply(value);

    private void ApplyFilters()
    {
        var query = SearchText.Trim();
        var filtered = Computers.Where(x => string.IsNullOrWhiteSpace(query) || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        filtered = StationFilter switch
        {
            "Disponibles" => filtered.Where(x => x.IsAvailable),
            "En uso" => filtered.Where(x => x.IsInUse),
            "Fuera de servicio" => filtered.Where(x => x.IsOutOfService),
            _ => filtered
        };
        VisibleComputers.Clear();
        foreach (var card in filtered) VisibleComputers.Add(card);
    }

    private void ApplyHistoryFilters()
    {
        var filtered = History.AsEnumerable();
        if (HistoryComputerFilter != "Todos")
            filtered = filtered.Where(x => string.Equals(x.ComputerName, HistoryComputerFilter, StringComparison.OrdinalIgnoreCase));
        if (HistoryPaymentFilter == "Pendiente")
            filtered = filtered.Where(x => x.PaymentStatus == PaymentStatus.Pending);
        else if (HistoryPaymentFilter == "Pagada")
            filtered = filtered.Where(x => x.PaymentStatus == PaymentStatus.Paid);
        if (HistoryModeFilter == "Tiempo libre")
            filtered = filtered.Where(x => x.Mode == SessionMode.OpenEnded);
        else if (HistoryModeFilter == "Tiempo contratado")
            filtered = filtered.Where(x => x.Mode == SessionMode.Prepaid);
        if (HistoryStateFilter == "Activa")
            filtered = filtered.Where(x => x.State == SessionState.Active);
        else if (HistoryStateFilter == "Completada")
            filtered = filtered.Where(x => x.State == SessionState.Completed);
        else if (HistoryStateFilter == "Tiempo agotado")
            filtered = filtered.Where(x => x.State == SessionState.TimeExpired);
        if (HistoryFromDate is { } from)
            filtered = filtered.Where(x => x.StartedAtUtc.ToLocalTime().Date >= from.Date);
        if (HistoryToDate is { } to)
            filtered = filtered.Where(x => x.StartedAtUtc.ToLocalTime().Date <= to.Date);

        VisibleHistory.Clear();
        foreach (var item in filtered) VisibleHistory.Add(item);
    }

    [RelayCommand]
    private void Navigate(string? section)
    {
        if (!string.IsNullOrWhiteSpace(section)) CurrentSection = section;
    }

    private static ComputerCardViewModel CreateCard(
        Computer computer,
        IReadOnlyDictionary<Guid, SessionSummary> activeSessions,
        IReadOnlyList<Tariff> tariffs) =>
        new(computer, activeSessions.GetValueOrDefault(computer.Id), tariffs);

    [RelayCommand]
    private async Task AddComputerAsync()
    {
        try
        {
            await _service.AddComputerAsync(NewComputerName);
            NewComputerName = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddTariffAsync()
    {
        try
        {
            if (!decimal.TryParse(NewTariffPrice, NumberStyles.Number, CultureInfo.CurrentCulture, out var price))
                throw new ArgumentException("Ingresa un precio válido.");

            await _service.AddTariffAsync(NewTariffName, price);
            NewTariffName = string.Empty;
            NewTariffPrice = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    [RelayCommand]
    private async Task StartSessionAsync(ComputerCardViewModel? card)
    {
        if (card is null) return;
        try
        {
            var tariffId = card.SelectedTariffId ?? throw new InvalidOperationException("Configura una tarifa activa antes de iniciar sesiones.");
            var duration = card.SelectedMode == SessionMode.Prepaid ? TimeSpan.FromMinutes(card.SelectedDurationMinutes) : (TimeSpan?)null;
            await _service.StartSessionAsync(card.Id, tariffId, card.SelectedMode, duration, card.PayAtStart);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    [RelayCommand]
    private async Task FinishSessionAsync(ComputerCardViewModel? card)
    {
        if (card is null) return;
        try
        {
            if (card.ActiveSessionId.HasValue)
            {
                await _service.FinishSessionAsync(card.ActiveSessionId.Value);
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ToggleServiceAsync(ComputerCardViewModel? card)
    {
        if (card is null) return;
        try
        {
            if (card.Status == ComputerStatus.OutOfService)
                await _service.RestoreComputerAsync(card.Id);
            else
                await _service.SetComputerOutOfServiceAsync(card.Id);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RenameComputerAsync(ComputerCardViewModel? card)
    {
        if (card is null) return;
        try
        {
            await _service.RenameComputerAsync(card.Id, card.EditedName);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RegisterPaymentAsync(SessionSummary? session)
    {
        if (session is null) return;
        try
        {
            await _service.RegisterPaymentAsync(session.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    public void RefreshActiveDurations()
    {
        foreach (var computer in Computers)
            computer.Refresh();
    }
}

public partial class ComputerCardViewModel : ObservableObject
{
    private readonly Computer _computer;
    private readonly SessionSummary? _activeSession;

    [ObservableProperty] private Guid? selectedTariffId;
    [ObservableProperty] private SessionMode selectedMode = SessionMode.OpenEnded;
    [ObservableProperty] private int selectedDurationMinutes = 60;
    [ObservableProperty] private bool payAtStart;
    [ObservableProperty] private string editedName = string.Empty;

    public IReadOnlyList<SessionMode> SessionModes { get; } = Enum.GetValues<SessionMode>();
    public IReadOnlyList<int> ContractDurations { get; } = [15, 30, 45, 60, 120];

    public ComputerCardViewModel(Computer computer, SessionSummary? activeSession, IReadOnlyList<Tariff> tariffs)
    {
        _computer = computer;
        _activeSession = activeSession;
        EditedName = computer.Name;
        Tariffs = tariffs;
        SelectedTariffId = tariffs.FirstOrDefault(x => x.IsActive)?.Id;
    }

    public Guid Id => _computer.Id;
    public string Name => _computer.Name;
    public ComputerStatus Status => _computer.Status;
    public string StatusText => Status switch
    {
        ComputerStatus.Available => "Disponible",
        ComputerStatus.InUse => "En uso",
        _ => "Fuera de servicio"
    };
    public bool IsInUse => Status == ComputerStatus.InUse;
    public bool IsAvailable => Status == ComputerStatus.Available;
    public bool IsOutOfService => Status == ComputerStatus.OutOfService;
    public Guid? ActiveSessionId => _activeSession?.Id;
    public string SessionModeText => _activeSession?.Mode switch
    {
        SessionMode.Prepaid => "Tiempo contratado",
        SessionMode.OpenEnded => "Tiempo libre",
        _ => "Sin sesión"
    };
    public string PaymentText => _activeSession?.PaymentStatus == PaymentStatus.Paid ? "Pagada" : "Pendiente de cobro";
    public string SessionText => _activeSession is null
        ? "Sin sesión activa"
        : _activeSession.State == SessionState.TimeExpired
            ? $"Tiempo agotado · {_activeSession.Amount:C2}"
        : _activeSession.Mode == SessionMode.Prepaid && RemainingDuration == TimeSpan.Zero
                ? $"Tiempo agotado · {_activeSession.Amount:C2}"
                : _activeSession.Mode == SessionMode.Prepaid
                ? $"Restante: {RemainingDuration:hh\\:mm\\:ss} · {_activeSession.Amount:C2}"
                : $"Transcurrido: {CurrentDuration:hh\\:mm\\:ss} · Estimado: {CurrentAmount:C2}";
    public IReadOnlyList<Tariff> Tariffs { get; }
    public decimal ContractedAmount
    {
        get
        {
            var tariff = SelectedTariffId is { } id ? Tariffs.FirstOrDefault(x => x.Id == id) : null;
            return tariff is null || SelectedDurationMinutes <= 0 ? 0m : Session.CalculateAmount(TimeSpan.FromMinutes(SelectedDurationMinutes), tariff.PricePerHour);
        }
    }

    partial void OnSelectedTariffIdChanged(Guid? value) => OnPropertyChanged(nameof(ContractedAmount));
    partial void OnSelectedDurationMinutesChanged(int value) => OnPropertyChanged(nameof(ContractedAmount));

    private TimeSpan CurrentDuration => _activeSession is null
        ? TimeSpan.Zero
        : DateTime.UtcNow - _activeSession.StartedAtUtc;

    private decimal CurrentAmount => _activeSession is null
        ? 0m
        : Session.CalculateAmount(CurrentDuration, _activeSession.HourlyRate);
    private TimeSpan RemainingDuration => _activeSession is null || _activeSession.Mode != SessionMode.Prepaid
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(Math.Max(0, (_activeSession.ContractedDuration ?? _activeSession.Duration).TotalSeconds - CurrentDuration.TotalSeconds));

    public void Refresh() => OnPropertyChanged(nameof(SessionText));
}
