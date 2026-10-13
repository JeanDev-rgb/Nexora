using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexora.Core.Enums;
using Nexora.Core.Interfaces;
using Nexora.Core.Models;

namespace Nexora.Desktop.ViewModels;

public partial class CashViewModel : ObservableObject
{
    private readonly INexoraService _service;
    private readonly Func<Guid, Task> _registerPayment;

    [ObservableProperty] private string computerFilter = "Todos";
    [ObservableProperty] private string paymentFilter = "Todos";
    [ObservableProperty] private DateTime? fromDate;
    [ObservableProperty] private DateTime? toDate;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string message = string.Empty;
    [ObservableProperty] private decimal confirmedTotal;
    [ObservableProperty] private decimal pendingTotal;

    public ObservableCollection<SessionSummary> Entries { get; } = [];
    public ObservableCollection<CashEntryViewModel> VisibleEntries { get; } = [];
    public ObservableCollection<string> ComputerOptions { get; } = ["Todos"];
    public IReadOnlyList<string> PaymentOptions { get; } = ["Todos", "Pendiente", "Pagada"];

    public CashViewModel(INexoraService service, Func<Guid, Task> registerPayment)
    {
        _service = service;
        _registerPayment = registerPayment;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var entries = await _service.GetCashEntriesAsync(cancellationToken);
            Entries.Clear();
            foreach (var entry in entries) Entries.Add(entry);
            ComputerOptions.Clear();
            ComputerOptions.Add("Todos");
            foreach (var name in entries.Select(x => x.ComputerName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                ComputerOptions.Add(name);
            ApplyFilters();
            Message = entries.Count == 0 ? "No hay operaciones de caja." : string.Empty;
        }
        catch (Exception ex)
        {
            Message = $"No se pudo cargar la caja: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnComputerFilterChanged(string value) => ApplyFilters();
    partial void OnPaymentFilterChanged(string value) => ApplyFilters();
    partial void OnFromDateChanged(DateTime? value) => ApplyFilters();
    partial void OnToDateChanged(DateTime? value) => ApplyFilters();

    private void ApplyFilters()
    {
        var rows = Entries.AsEnumerable();
        if (ComputerFilter != "Todos")
            rows = rows.Where(x => string.Equals(x.ComputerName, ComputerFilter, StringComparison.OrdinalIgnoreCase));
        if (PaymentFilter == "Pendiente")
            rows = rows.Where(x => x.PaymentStatus == PaymentStatus.Pending);
        else if (PaymentFilter == "Pagada")
            rows = rows.Where(x => x.PaymentStatus == PaymentStatus.Paid);

        rows = rows.Where(IsWithinSelectedDates);
        var visible = rows.ToList();
        VisibleEntries.Clear();
        foreach (var row in visible) VisibleEntries.Add(new CashEntryViewModel(row));
        ConfirmedTotal = visible.Where(x => x.PaymentStatus == PaymentStatus.Paid).Sum(x => x.Amount);
        PendingTotal = visible.Where(x => x.PaymentStatus == PaymentStatus.Pending).Sum(x => x.Amount);
        if (Entries.Count > 0 && visible.Count == 0)
            Message = "No hay operaciones que coincidan con los filtros.";
        else if (visible.Count > 0)
            Message = string.Empty;
    }

    private bool IsWithinSelectedDates(SessionSummary entry)
    {
        // Confirmed cash is dated by the persisted charge; unpaid obligations use
        // the session end date, or the start date for a prepaid session still in use.
        var effectiveUtc = entry.PaymentStatus == PaymentStatus.Paid
            ? entry.ChargedAtUtc ?? entry.StartedAtUtc
            : entry.EndedAtUtc ?? entry.StartedAtUtc;
        var localDate = effectiveUtc.ToLocalTime().Date;
        return (!FromDate.HasValue || localDate >= FromDate.Value.Date)
            && (!ToDate.HasValue || localDate <= ToDate.Value.Date);
    }

    [RelayCommand]
    private async Task RegisterPaymentAsync(CashEntryViewModel? entry)
    {
        if (entry is null || !entry.Summary.IsPending) return;
        try
        {
            await _registerPayment(entry.Summary.Id);
            Message = "Cobro registrado.";
        }
        catch (Exception ex)
        {
            Message = $"No se pudo registrar el cobro: {ex.Message}";
        }
    }
}

public sealed class CashEntryViewModel(SessionSummary summary)
{
    public SessionSummary Summary { get; } = summary;
    public string ComputerName => Summary.ComputerName;
    public string TariffName => Summary.TariffName;
    public decimal Amount => Summary.Amount;
    public PaymentStatus PaymentStatus => Summary.PaymentStatus;
    public string PaymentStatusText => Summary.PaymentStatus == PaymentStatus.Paid ? "Cobrado" : "Pendiente";
    public bool IsPending => Summary.IsPending;
    public DateTime CashDate
    {
        get
        {
            var utc = Summary.PaymentStatus == PaymentStatus.Paid
                ? Summary.ChargedAtUtc ?? Summary.StartedAtUtc
                : Summary.EndedAtUtc ?? Summary.StartedAtUtc;
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        }
    }
}
