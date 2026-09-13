using Avtomagazin.Admin;
using Avtomagazin.ApiClient;
using Avtomagazin.Contracts;
using Microsoft.JSInterop;

namespace Avtomagazin.Admin.Components;

public partial class DispatchPanel
{
    private List<VehicleDto> _vans = [];
    private List<CaseSummaryDto> _cases = [];
    private List<RouteDto> _routes = [];
    private List<DriverNoteDto> _notes = [];
    private readonly HashSet<Guid> _seenNoteIds = [];
    private bool _notesPrimed;
    private Guid? _tripVehicleId;
    private CaseDetailDto? _detail;
    private string _comment = "";
    private string _statusFilter = "active";
    private string _settlementQuery = "";
    private string? _caseError;
    private bool _busy;
    private string? _loadError;
    private bool _loadErrorShown;
    private bool _notesDegradedShown;
    private CancellationTokenSource? _poll;

    private int _liveCount => ActiveRoutes.Count(r => r.Live);

    private IEnumerable<DriverNoteDto> FilteredNotes
    {
        get
        {
            IEnumerable<DriverNoteDto> items = _notes.OrderByDescending(n => n.CreatedAtUtc);
            if (string.IsNullOrWhiteSpace(_settlementQuery))
            {
                return items;
            }

            var q = _settlementQuery.Trim();
            return items.Where(n =>
            {
                var van = VanOf(n.VehicleId);
                var route = RouteOf(n.VehicleId);
                return n.Body.Contains(q, StringComparison.OrdinalIgnoreCase)
                       || (van?.PlateNumber.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                       || (van?.OperatorName.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                       || (route?.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
            });
        }
    }

    private IEnumerable<CaseSummaryDto> FilteredCases
    {
        get
        {
            IEnumerable<CaseSummaryDto> items = _cases;
            if (!string.IsNullOrWhiteSpace(_settlementQuery))
            {
                var q = _settlementQuery.Trim();
                items = items.Where(c =>
                    c.SettlementName.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || c.SettlementKey.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            return items
                .OrderByDescending(c => c.Status != CaseStatuses.Closed)
                .ThenByDescending(c => c.ReportCount)
                .ThenByDescending(c => c.UpdatedAtUtc);
        }
    }

    private IEnumerable<RouteProgress> ActiveRoutes
        => DispatchBoard.ActiveRoutes(_routes, _vans, _notes);

    protected override async Task OnInitializedAsync()
    {
        await ReloadAsync();
        _poll = new CancellationTokenSource();
        _ = LoopAsync(_poll.Token);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await ReloadAsync();
            await InvokeAsync(StateHasChanged);

            try
            {
                await Task.Delay(6000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReloadAsync()
    {
        var status = _statusFilter switch
        {
            "active" => $"{CaseStatuses.Open},{CaseStatuses.InProgress}",
            "all" => $"{CaseStatuses.Open},{CaseStatuses.InProgress},{CaseStatuses.Closed}",
            _ => _statusFilter
        };

        try
        {
            var snap = await Api.GetDispatchSnapshotAsync(status);
            _vans = snap.Vehicles;
            _cases = snap.Cases;
            _routes = snap.Routes;
            if (snap.NotesUnavailable)
            {
                if (!_notesDegradedShown)
                {
                    _notesDegradedShown = true;
                    Toasts.Error("Заметки водителей недоступны");
                }
            }
            else
            {
                _notesDegradedShown = false;
                await ApplyNotesAsync(snap.DriverNotes);
            }

            _loadError = null;
            _loadErrorShown = false;
        }
        catch (SessionExpiredException)
        {
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            if (!_loadErrorShown)
            {
                _loadErrorShown = true;
                Toasts.Error($"Мониторинг: {ex.Message}");
            }
        }
    }

    private async Task ApplyNotesAsync(List<DriverNoteDto> notes)
    {
        var incoming = notes ?? [];
        if (_notesPrimed)
        {
            foreach (var note in incoming.Where(n => !_seenNoteIds.Contains(n.Id)))
            {
                var plate = VanOf(note.VehicleId)?.PlateNumber ?? "автолавка";
                var route = RouteOf(note.VehicleId)?.Name;
                var where = string.IsNullOrWhiteSpace(route) ? plate : $"{route} · {plate}";
                Toasts.Error($"Проблема на маршруте · {where}: {note.Body}");
                await PlaySoftAlertAsync();
            }
        }

        _notes = incoming;
        _seenNoteIds.Clear();
        foreach (var id in incoming.Select(n => n.Id))
        {
            _seenNoteIds.Add(id);
        }

        _notesPrimed = true;
    }

    private async Task PlaySoftAlertAsync()
    {
        try
        {
            await Js.InvokeVoidAsync("avtomagazinNotify.softAlert");
        }
        catch
        {
        }
    }

    private async Task SetStatusFilterAsync(string filter)
    {
        if (_statusFilter == filter)
        {
            return;
        }

        _statusFilter = filter;
        await ReloadAsync();
    }

    private Task OpenAttentionCaseAsync(CaseSummaryDto item)
        => item.VehicleId is Guid vehicleId
            ? OpenTripAsync(vehicleId)
            : OpenCaseAsync(item.Id);

    private async Task OpenTripAsync(Guid vehicleId)
    {
        _detail = null;
        _caseError = null;
        _tripVehicleId = vehicleId;
        await SetBodyLockAsync(true);
    }

    private async Task CloseTripAsync()
    {
        _tripVehicleId = null;
        if (_detail is null)
        {
            await SetBodyLockAsync(false);
        }
    }

    private async Task OpenCaseAsync(Guid id)
    {
        _tripVehicleId = null;
        _caseError = null;
        _comment = "";
        try
        {
            _detail = await Api.GetCaseAsync(id);
            await SetBodyLockAsync(true);
        }
        catch (Exception ex)
        {
            _caseError = ex.Message;
            await SetBodyLockAsync(false);
        }
    }

    private async Task CloseCase()
    {
        _detail = null;
        _caseError = null;
        await SetBodyLockAsync(false);
    }

    private async Task SetBodyLockAsync(bool locked)
    {
        try
        {
            await Js.InvokeVoidAsync("avtomagazinNotify.setBodyLock", locked);
        }
        catch
        {
        }
    }

    private async Task AddCommentAsync()
    {
        if (_detail is null || string.IsNullOrWhiteSpace(_comment))
        {
            return;
        }

        _busy = true;
        _caseError = null;
        try
        {
            _detail = await Api.AddCaseCommentAsync(_detail.Id, _comment.Trim());
            _comment = "";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _caseError = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private Task MarkInProgressAsync() => SetStatusAsync(CaseStatuses.InProgress);
    private Task CloseCaseStatusAsync() => SetStatusAsync(CaseStatuses.Closed);
    private Task ReopenCaseAsync() => SetStatusAsync(CaseStatuses.Open);

    private async Task SetStatusAsync(string status)
    {
        if (_detail is null)
        {
            return;
        }

        _busy = true;
        _caseError = null;
        try
        {
            var note = string.IsNullOrWhiteSpace(_comment) ? null : _comment.Trim();
            _detail = await Api.SetCaseStatusAsync(_detail.Id, status, note);
            _comment = "";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _caseError = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private VehicleDto? VanOf(Guid? vehicleId) => DispatchBoard.VanOf(_vans, vehicleId);

    private RouteDto? RouteOf(Guid vehicleId) => RouteDto.ForVehicle(_routes, vehicleId);

    private DriverNoteDto? NoteOf(Guid vehicleId)
        => _notes.FirstOrDefault(n => n.VehicleId == vehicleId);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await SetBodyLockAsync(false);
        }
        catch (JSDisconnectedException)
        {
        }

        _poll?.Cancel();
        _poll?.Dispose();
    }
}
