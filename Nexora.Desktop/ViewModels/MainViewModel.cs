using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexora.Core.Enums;
using Nexora.Core.Interfaces;
using Nexora.Core.Models;

namespace Nexora.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly INexoraService _service;
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

    public ObservableCollection<ComputerCardViewModel> Computers { get; } = [];
    public ObservableCollection<Tariff> Tariffs { get; } = [];
    public ObservableCollection<SessionSummary> History { get; } = [];

    public MainViewModel(INexoraService service) => _service = service;

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            await _service.InitializeAsync();
            var computers = await _service.GetComputersAsync();
            var tariffs = await _service.GetTariffsAsync();
            var history = await _service.GetRecentSessionsAsync();
            var dashboard = await _service.GetDashboardAsync(DateTime.Now, _timeZone);

            Computers.Clear();
            foreach (var computer in computers)
                Computers.Add(await CreateCardAsync(computer, tariffs));

            Tariffs.Clear();
            foreach (var tariff in tariffs)
                Tariffs.Add(tariff);

            History.Clear();
            foreach (var item in history)
                History.Add(item);

            TotalComputers = dashboard.TotalComputers;
            ActiveSessions = dashboard.ActiveSessions;
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

    private async Task<ComputerCardViewModel> CreateCardAsync(Computer computer, IReadOnlyList<Tariff> tariffs) =>
        new(computer, await _service.GetActiveSessionAsync(computer.Id), tariffs);

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

    public IReadOnlyList<SessionMode> SessionModes { get; } = Enum.GetValues<SessionMode>();
    public IReadOnlyList<int> ContractDurations { get; } = [15, 30, 45, 60, 120];

    public ComputerCardViewModel(Computer computer, SessionSummary? activeSession, IReadOnlyList<Tariff> tariffs)
    {
        _computer = computer;
        _activeSession = activeSession;
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
        : TimeSpan.FromSeconds(Math.Max(0, _activeSession.Duration.TotalSeconds - CurrentDuration.TotalSeconds));

    public void Refresh() => OnPropertyChanged(nameof(SessionText));
}
