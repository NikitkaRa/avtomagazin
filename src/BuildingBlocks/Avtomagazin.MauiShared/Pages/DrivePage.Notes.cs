using Avtomagazin.ApiClient;

namespace Avtomagazin.MauiShared;

public partial class DrivePage
{
    private async void OnNoteStuck(object? sender, EventArgs e)
    {
        if (_hasActiveNote)
        {
            return;
        }

        await SendNoteAsync("Завяз");
    }

    private async void OnNoteTire(object? sender, EventArgs e)
    {
        if (_hasActiveNote)
        {
            return;
        }

        await SendNoteAsync("Пробил колесо");
    }

    private async void OnNoteWait(object? sender, EventArgs e)
    {
        if (_hasActiveNote)
        {
            return;
        }

        await SendNoteAsync("Стою на месте");
    }

    private async void OnNoteAction(object? sender, EventArgs e)
    {
        if (_hasActiveNote)
        {
            await ClearNoteAsync();
            return;
        }

        var text = NoteEntry.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            NoteStatus.TextColor = Color.FromArgb("#F0C7B0");
            NoteStatus.Text = "Напишите текст или выберите быстрый вариант";
            return;
        }

        await SendNoteAsync(text);
    }

    private async Task ClearNoteAsync()
    {
        var van = Vehicle();
        if (van is null || _noteBusy)
        {
            return;
        }

        _noteBusy = true;
        PaintNoteAction();
        try
        {
            await _api.Client.ClearDriverNoteAsync(van.Id);
            NoteEntry.Text = "";
            NoteStatus.TextColor = Color.FromArgb("#A7B8AD");
            NoteStatus.Text = "Сообщение снято";
            _hasActiveNote = false;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            NoteStatus.TextColor = Color.FromArgb("#F0C7B0");
            NoteStatus.Text = ApiErrors.Friendly(ex);
            PaintLinkBanner();
        }
        finally
        {
            _noteBusy = false;
            PaintNoteAction();
        }
    }

    private void PaintNoteAction()
    {
        var van = Vehicle();
        var note = _snapshot.ActiveNotes().FirstOrDefault(n => van is not null && n.VehicleId == van.Id);
        _hasActiveNote = note is not null;
        var locked = _hasActiveNote || _noteBusy;
        SetNoteChip(NoteChipStuck, !locked);
        SetNoteChip(NoteChipTire, !locked);
        SetNoteChip(NoteChipWait, !locked);
        NoteEntry.IsEnabled = !locked;
        NoteEntry.Opacity = locked ? 0.45 : 1;
        if (_hasActiveNote)
        {
            NoteActionBtn.Text = "Снять";
            NoteActionBtn.BackgroundColor = Color.FromArgb("#1B3328");
            NoteActionBtn.TextColor = Color.FromArgb("#F3F7F3");
        }
        else
        {
            NoteActionBtn.Text = "Отправить";
            NoteActionBtn.BackgroundColor = Color.FromArgb("#3DBA7A");
            NoteActionBtn.TextColor = Color.FromArgb("#082014");
        }
    }

    private static void SetNoteChip(Button button, bool on)
    {
        button.IsEnabled = on;
        button.Opacity = on ? 1 : 0.45;
    }

    private async Task SendNoteAsync(string body)
    {
        if (_hasActiveNote || _noteBusy)
        {
            NoteStatus.TextColor = Color.FromArgb("#F0C7B0");
            NoteStatus.Text = "Сначала снимите текущее сообщение";
            return;
        }

        var van = Vehicle();
        if (van is null)
        {
            NoteStatus.TextColor = Color.FromArgb("#F0C7B0");
            NoteStatus.Text = "Нет автолавки";
            return;
        }

        _noteBusy = true;
        PaintNoteAction();
        try
        {
            var (lat, lng) = await ResolveNoteCoordsAsync(van);
            await _api.Client.PostDriverNoteAsync(new PostDriverNoteRequest(van.Id, body, lat, lng));
            NoteEntry.Text = body;
            NoteStatus.TextColor = Color.FromArgb("#3DBA7A");
            NoteStatus.Text = $"Отправлено диспетчеру · {BelarusTime.Clock(DateTimeOffset.UtcNow)}";
            _hasActiveNote = true;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            NoteStatus.TextColor = Color.FromArgb("#F0C7B0");
            NoteStatus.Text = ApiErrors.Friendly(ex);
            PaintLinkBanner();
        }
        finally
        {
            _noteBusy = false;
            PaintNoteAction();
        }
    }

    private async Task<(double Lat, double Lng)> ResolveNoteCoordsAsync(VehicleDto van)
    {
        try
        {
            var loc = await Geolocation.GetLastKnownLocationAsync()
                      ?? await Geolocation.GetLocationAsync(
                          new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(6)));
            if (loc is not null)
            {
                return (loc.Latitude, loc.Longitude);
            }
        }
        catch
        {
            // fall back to last known van fix
        }

        if (van.LastLatitude is double lat && van.LastLongitude is double lng)
        {
            return (lat, lng);
        }

        throw new InvalidOperationException("Нет координат — включите GPS");
    }
}
