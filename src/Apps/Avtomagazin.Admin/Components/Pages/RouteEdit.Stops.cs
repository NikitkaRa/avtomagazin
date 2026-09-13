using Microsoft.AspNetCore.Components.Forms;

namespace Avtomagazin.Admin.Components.Pages;

public partial class RouteEdit
{
    private void SetPlanLocal(int index, string? value)
    {
        if (index < 0 || index >= _stops.Count || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!DateTime.TryParse(value, out var local))
        {
            return;
        }

        _stops[index].PlannedArrivalUtc = BelarusTime.ToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
    }

    private async Task OnStopPhotoSelected(int index, InputFileChangeEventArgs e)
    {
        if (index < 0 || index >= _stops.Count)
        {
            return;
        }

        try
        {
            var file = e.File;
            if (file.Size > 400_000)
            {
                Fail("Фото остановки больше 400 КБ — сожмите и попробуйте снова");
                return;
            }

            await using var stream = file.OpenReadStream(maxAllowedSize: 400_000);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var b64 = Convert.ToBase64String(ms.ToArray());
            var mime = string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType;
            _stops[index].PhotoDataUrl = $"data:{mime};base64,{b64}";
            _stops[index].ClearPhoto = false;
            _stops[index].PhotoDirty = true;
            Ok("Фото выбрано — нажмите «Сохранить», чтобы записать на сервер");
        }
        catch (Exception ex)
        {
            Fail(ex.Message.Contains("401", StringComparison.Ordinal)
                ? "Не удалось загрузить файл в браузер. Обновите страницу и войдите снова."
                : ex.Message);
        }
    }

    private void ClearStopPhoto(int index)
    {
        if (index < 0 || index >= _stops.Count)
        {
            return;
        }

        _stops[index].PhotoDataUrl = null;
        _stops[index].ClearPhoto = true;
        _stops[index].PhotoDirty = true;
    }

    private async Task RenameStop(int index, string? name)
    {
        if (index < 0 || index >= _stops.Count)
        {
            return;
        }

        _stops[index].Name = string.IsNullOrWhiteSpace(name) ? _stops[index].Name : name.Trim();
        await DrawRouteAsync(fit: false);
    }

    private async Task RemoveStop(int index)
    {
        if (index < 0 || index >= _stops.Count)
        {
            return;
        }

        _stops.RemoveAt(index);
        await DrawRouteAsync(fit: false);
    }

    private async Task MoveUp(int index)
    {
        if (index <= 0 || index >= _stops.Count)
        {
            return;
        }

        (_stops[index - 1], _stops[index]) = (_stops[index], _stops[index - 1]);
        await DrawRouteAsync(fit: false);
    }

    private async Task MoveDown(int index)
    {
        if (index < 0 || index >= _stops.Count - 1)
        {
            return;
        }

        (_stops[index + 1], _stops[index]) = (_stops[index], _stops[index + 1]);
        await DrawRouteAsync(fit: false);
    }
}
