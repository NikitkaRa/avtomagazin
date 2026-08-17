using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Avtomagazin.ApiClient;

public interface IAccessTokenAccessor
{
    string? AccessToken { get; }

    /// <summary>Called when an API request returns 401. Default: no-op.</summary>
    void NotifyUnauthorized()
    {
    }
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAvtomagazinApiClient(this IServiceCollection services, string gatewayBaseUrl)
    {
        services.Configure<JsonSerializerOptions>(o =>
        {
            o.PropertyNameCaseInsensitive = true;
            o.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddScoped(sp =>
        {
            var http = new HttpClient
            {
                BaseAddress = new Uri(gatewayBaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
            return new AvtomagazinClient(http, sp.GetService<IAccessTokenAccessor>());
        });

        return services;
    }
}

public sealed class AvtomagazinClient(HttpClient http, IAccessTokenAccessor? tokens = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private void ApplyAuth()
    {
        if (tokens?.AccessToken is { Length: > 0 } token)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            http.DefaultRequestHeaders.Remove("Authorization");
        }
    }

    public Task<LoginResponse?> LoginAsync(string email, string password, CancellationToken ct = default)
        => AuthAsync("identity/api/auth/login", new { email, password }, ct);

    public Task<LoginResponse?> RegisterAsync(
        string email,
        string password,
        string? name,
        string client,
        string? staffRole = null,
        CancellationToken ct = default)
        => AuthAsync("identity/api/auth/register", new { email, password, name, client, staffRole }, ct);

    private async Task<LoginResponse?> AuthAsync(string path, object body, CancellationToken ct)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync(path, body, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException("Этот email уже зарегистрирован.");
        }

        if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden)
        {
            var payload = await response.Content.ReadFromJsonAsync<ErrorPayload>(JsonOptions, ct);
            throw new InvalidOperationException(payload?.Error ?? "Проверь email и пароль.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, ct);
    }

    public Task<List<AccountDto>> GetUsersAsync(string? status = null, CancellationToken ct = default)
    {
        var path = string.IsNullOrWhiteSpace(status)
            ? "identity/api/users"
            : $"identity/api/users?status={Uri.EscapeDataString(status)}";
        return GetListAsync<AccountDto>(path, ct);
    }

    public Task<List<StaffMemberDto>> GetStaffAsync(CancellationToken ct = default)
        => GetListAsync<StaffMemberDto>("identity/api/staff", ct);

    public async Task AssignUserVehicleAsync(Guid userId, Guid? vehicleId, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync(
            $"identity/api/users/{userId}/assign-vehicle",
            new { vehicleId },
            ct);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var payload = await response.Content.ReadFromJsonAsync<ErrorPayload>(JsonOptions, ct);
            throw new InvalidOperationException(payload?.Error ?? "Не удалось назначить автолавку");
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task<AccountDto?> ApproveUserAsync(Guid id, string? role = null, Guid? vehicleId = null, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync($"identity/api/users/{id}/approve", new { role, vehicleId }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AccountDto>(JsonOptions, ct);
    }

    public async Task RejectUserAsync(Guid id, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync($"identity/api/users/{id}/reject", new { }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DisableUserAsync(Guid id, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync($"identity/api/users/{id}/disable", new { }, ct);
        response.EnsureSuccessStatusCode();
    }

    public Task<LoginResponse?> ChangePasswordAsync(string current, string next, CancellationToken ct = default)
        => AuthAsync("identity/api/auth/password", new { current, next }, ct);

    public async Task<StaffProfileDto?> GetProfileAsync(CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.GetAsync("identity/api/auth/me", ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<StaffProfileDto>(JsonOptions, ct);
    }

    public async Task<StaffProfileDto?> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PatchAsJsonAsync("identity/api/auth/profile", request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<StaffProfileDto>(JsonOptions, ct);
    }

    public async Task ResetUserPasswordAsync(Guid id, string password, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync($"identity/api/users/{id}/password", new { password }, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var payload = await response.Content.ReadFromJsonAsync<ErrorPayload>(JsonOptions, ct);
            throw new InvalidOperationException(payload?.Error ?? "Пароль не принят.");
        }

        response.EnsureSuccessStatusCode();
    }

    public Task<List<VehicleDto>> GetVehiclesAsync(CancellationToken ct = default)
        => GetListAsync<VehicleDto>("fleet/api/vehicles", ct);

    public async Task<VehicleDto?> GetVehicleAsync(Guid id, CancellationToken ct = default)
    {
        ApplyAuth();
        return await http.GetFromJsonAsync<VehicleDto>($"fleet/api/vehicles/{id}", JsonOptions, ct);
    }

    public async Task<VehicleDto?> CreateVehicleAsync(UpsertVehicleRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync("fleet/api/vehicles", request, ct);
        await EnsureVehicleOkAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<VehicleDto>(JsonOptions, ct);
    }

    public async Task<VehicleDto?> UpdateVehicleAsync(Guid id, UpsertVehicleRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PutAsJsonAsync($"fleet/api/vehicles/{id}", request, ct);
        await EnsureVehicleOkAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<VehicleDto>(JsonOptions, ct);
    }

    public async Task SetVehicleActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PatchAsJsonAsync($"fleet/api/vehicles/{id}/active", new { isActive }, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task EnsureVehicleOkAsync(HttpResponseMessage response, CancellationToken ct)
        => await EnsureSuccessAsync(response, ct);

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            tokens?.NotifyUnauthorized();
            throw new SessionExpiredException();
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("Недостаточно прав для этого действия.");
        }

        if (response.StatusCode is System.Net.HttpStatusCode.BadGateway or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.GatewayTimeout)
        {
            throw new InvalidOperationException("Сервис временно недоступен. Попробуйте ещё раз.");
        }

        var payload = await response.Content.ReadFromJsonAsync<ErrorPayload>(JsonOptions, ct);
        throw new InvalidOperationException(payload?.Error ?? $"Ошибка {(int)response.StatusCode}");
    }

    public async Task IngestPositionAsync(Guid vehicleId, double latitude, double longitude, double? speedKmh = null, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync(
            $"fleet/api/vehicles/{vehicleId}/positions",
            new { latitude, longitude, speedKmh, source = "driver-app" },
            ct);
        await EnsureSuccessAsync(response, ct);
    }

    public Task<List<RouteDto>> GetRoutesAsync(CancellationToken ct = default)
        => GetRoutesAsync(catalog: false, ct);

    public Task<List<RouteDto>> GetRoutesAsync(bool catalog, CancellationToken ct = default)
        => GetListAsync<RouteDto>(catalog ? "routing/api/routes?catalog=true" : "routing/api/routes", ct);

    public async Task<RouteDto?> GetRouteAsync(Guid routeId, CancellationToken ct = default)
    {
        ApplyAuth();
        return await http.GetFromJsonAsync<RouteDto>($"routing/api/routes/{routeId}", JsonOptions, ct);
    }

    public async Task<RouteDto?> CreateRouteAsync(CreateRouteRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync("routing/api/routes", request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<RouteDto>(JsonOptions, ct);
    }

    public async Task<RouteDto?> UpdateRouteAsync(Guid routeId, UpdateRouteRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PatchAsJsonAsync($"routing/api/routes/{routeId}", request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<RouteDto>(JsonOptions, ct);
    }

    public async Task AddStopAsync(Guid routeId, CreateStopRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync($"routing/api/routes/{routeId}/stops", request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<RouteDto?> ReplaceRouteStopsAsync(Guid routeId, IEnumerable<ReplaceStopItem> stops, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PutAsJsonAsync(
            $"routing/api/routes/{routeId}/stops",
            new ReplaceStopsRequest(stops.ToList()),
            ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<RouteDto>(JsonOptions, ct);
    }

    public async Task DeleteRouteAsync(Guid routeId, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.DeleteAsync($"routing/api/routes/{routeId}", ct);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, ct);
        }
    }

    public async Task DeleteStopAsync(Guid stopId, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.DeleteAsync($"routing/api/stops/{stopId}", ct);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, ct);
        }
    }

    public async Task ChangeScheduleAsync(ChangeScheduleRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync("routing/api/schedule/change", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public Task<List<EtaDto>> GetEtaAsync(string? settlement = null, CancellationToken ct = default)
    {
        var path = string.IsNullOrWhiteSpace(settlement)
            ? "routing/api/eta"
            : $"routing/api/eta?settlement={Uri.EscapeDataString(settlement)}";
        return GetListAsync<EtaDto>(path, ct);
    }

    public Task<List<DriverNoteDto>> GetDriverNotesAsync(CancellationToken ct = default)
        => GetListAsync<DriverNoteDto>("routing/api/driver-notes", ct);

    public async Task PostDriverNoteAsync(PostDriverNoteRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync("routing/api/driver-notes", request, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task ClearDriverNoteAsync(Guid vehicleId, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.DeleteAsync($"routing/api/driver-notes/{vehicleId}", ct);
        await EnsureSuccessAsync(response, ct);
    }

    public Task<List<RouteStopDto>> FindStopsAsync(string settlement, CancellationToken ct = default)
        => GetListAsync<RouteStopDto>($"routing/api/stops/{Uri.EscapeDataString(settlement)}", ct);

    public Task<List<CoverageDto>> GetCoverageAsync(string? regionCode = null, CancellationToken ct = default)
    {
        var path = string.IsNullOrWhiteSpace(regionCode)
            ? "routing/api/coverage"
            : $"routing/api/coverage?regionCode={Uri.EscapeDataString(regionCode)}";
        return GetListAsync<CoverageDto>(path, ct);
    }

    public async Task RecordCoverageAsync(CoverageVisitRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync("routing/api/coverage/visit", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ArriveAtStopAsync(Guid stopId, Guid vehicleId, bool skipped = false, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync(
            $"routing/api/stops/{stopId}/arrived",
            new { vehicleId, skipped },
            ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ReportStopPresenceAsync(Guid stopId, string kind, string deviceToken, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync(
            $"routing/api/stops/{stopId}/reports",
            new { kind, deviceToken },
            ct);
        response.EnsureSuccessStatusCode();
    }

    public Task<List<FavoriteDto>> GetFavoritesAsync(CancellationToken ct = default)
        => GetListAsync<FavoriteDto>("notifications/api/favorites", ct);

    public Task<List<FavoriteStatsDto>> GetFavoriteStatsAsync(CancellationToken ct = default)
        => GetListAsync<FavoriteStatsDto>("notifications/api/favorites/stats", ct);

    public async Task AddFavoriteStopAsync(string deviceToken, Guid stopId, string settlementName, string platform = "web", CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync(
            "notifications/api/favorites",
            new { deviceToken, stopId, settlementName, platform },
            ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveFavoriteStopAsync(Guid stopId, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.DeleteAsync($"notifications/api/favorites/{stopId}", ct);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public Task<List<PresenceReportDto>> GetPresenceReportsAsync(CancellationToken ct = default)
        => GetListAsync<PresenceReportDto>("routing/api/presence-reports", ct);

    public Task<List<CaseSummaryDto>> GetCasesAsync(string? status = null, CancellationToken ct = default)
    {
        var path = string.IsNullOrWhiteSpace(status)
            ? "routing/api/cases"
            : $"routing/api/cases?status={Uri.EscapeDataString(status)}";
        return GetListAsync<CaseSummaryDto>(path, ct);
    }

    public async Task<CaseDetailDto?> GetCaseAsync(Guid id, CancellationToken ct = default)
    {
        ApplyAuth();
        return await http.GetFromJsonAsync<CaseDetailDto>($"routing/api/cases/{id}", JsonOptions, ct);
    }

    public async Task<CaseDetailDto?> AddCaseCommentAsync(Guid id, string body, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync($"routing/api/cases/{id}/comments", new { body }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaseDetailDto>(JsonOptions, ct);
    }

    public async Task<CaseDetailDto?> SetCaseStatusAsync(Guid id, string status, string? comment = null, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PatchAsJsonAsync($"routing/api/cases/{id}/status", new { status, comment }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaseDetailDto>(JsonOptions, ct);
    }

    public async Task UpdateVehicleContactsAsync(Guid id, UpdateVehicleContactsRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PatchAsJsonAsync($"fleet/api/vehicles/{id}/contacts", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RegisterDeviceAsync(DeviceRegistrationRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync("notifications/api/devices/register", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public Task<List<NotificationDto>> GetNotificationsAsync(string? settlement = null, CancellationToken ct = default)
    {
        var path = string.IsNullOrWhiteSpace(settlement)
            ? "notifications/api/notifications"
            : $"notifications/api/notifications?settlement={Uri.EscapeDataString(settlement)}";
        return GetListAsync<NotificationDto>(path, ct);
    }

    public async Task<SnapshotDto> GetSnapshotAsync(CancellationToken ct = default)
    {
        var vehiclesTask = SafeListAsync(() => GetVehiclesAsync(ct));
        var routesTask = SafeListAsync(() => GetRoutesAsync(ct));
        var etaTask = SafeListAsync(() => GetEtaAsync(ct: ct));
        var notesTask = SafeListAsync(() => GetDriverNotesAsync(ct));
        await Task.WhenAll(vehiclesTask, routesTask, etaTask, notesTask);
        var vehicles = await vehiclesTask;
        var routes = await routesTask;
        if (vehicles.Count == 0 && routes.Count == 0)
        {
            throw new HttpRequestException("Нет ответа от сервера (авто и маршруты недоступны)");
        }

        return new SnapshotDto(
            DateTimeOffset.UtcNow,
            vehicles,
            routes,
            await etaTask,
            await notesTask);
    }

    private static async Task<List<T>> SafeListAsync<T>(Func<Task<List<T>>> load)
    {
        try
        {
            return await load();
        }
        catch
        {
            return [];
        }
    }

    public async Task<DispatchSnapshotDto> GetDispatchSnapshotAsync(string? caseStatus = null, CancellationToken ct = default)
    {
        var vehiclesTask = GetVehiclesAsync(ct);
        var casesTask = GetCasesAsync(caseStatus, ct);
        var etaTask = GetEtaAsync(ct: ct);
        var routesTask = GetRoutesAsync(ct);
        var notesTask = SafeListAsync(() => GetDriverNotesAsync(ct));
        await Task.WhenAll(vehiclesTask, casesTask, etaTask, routesTask, notesTask);
        return new DispatchSnapshotDto(
            DateTimeOffset.UtcNow,
            await vehiclesTask,
            await casesTask,
            await etaTask,
            await routesTask,
            await notesTask);
    }

    private async Task<List<T>> GetListAsync<T>(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (tokens?.AccessToken is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        var items = await response.Content.ReadFromJsonAsync<List<T>>(JsonOptions, ct);
        return items ?? [];
    }
}

public sealed record LoginResponse(
    string? AccessToken,
    string Role,
    Guid UserId,
    string Email,
    string? Name,
    Guid? VehicleId,
    string? Status);

public sealed record StaffProfileDto(
    Guid Id,
    string Email,
    string Role,
    string? Name,
    string? DisplayName,
    string? LastName,
    string? FirstName,
    string? MiddleName,
    string? Phone,
    string? PhotoUrl,
    Guid? VehicleId,
    string? Status);

public sealed record UpdateProfileRequest(
    string? DisplayName = null,
    string? LastName = null,
    string? FirstName = null,
    string? MiddleName = null,
    string? Phone = null,
    string? PhotoDataUrl = null,
    bool? ClearPhoto = null);

public sealed record AccountDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    string RoleTitle,
    string Status,
    Guid? VehicleId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ApprovedAtUtc);

public sealed record StaffMemberDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    string RoleTitle,
    string Status,
    Guid? VehicleId);

internal sealed record ErrorPayload(string? Error);

public sealed record FavoriteDto(Guid StopId, string SettlementName, DateTimeOffset CreatedAtUtc);

public sealed record FavoriteStatsDto(Guid StopId, string SettlementName, int SubscriberCount);

public sealed record VehicleDto(
    Guid Id,
    string PlateNumber,
    string OperatorName,
    bool IsActive,
    double? LastLatitude,
    double? LastLongitude,
    DateTimeOffset? LastSeenAtUtc,
    string? LastSource,
    string? DriverName = null,
    string? DriverPhone = null,
    string? SellerName = null,
    string? SellerPhone = null,
    string? OperatorPhone = null,
    Guid? DriverUserId = null,
    Guid? SellerUserId = null,
    string? PhotoDataUrl = null);

public sealed record UpsertVehicleRequest(
    string PlateNumber,
    string OperatorName,
    string? DriverName = null,
    string? DriverPhone = null,
    string? SellerName = null,
    string? SellerPhone = null,
    string? OperatorPhone = null,
    Guid? DriverUserId = null,
    Guid? SellerUserId = null,
    string? PhotoDataUrl = null,
    bool? ClearPhoto = null,
    bool? IsActive = null);

public sealed record CreateVehicleRequest(string PlateNumber, string OperatorName);

public sealed record UpdateVehicleContactsRequest(
    string? DriverName,
    string? DriverPhone,
    string? SellerName,
    string? SellerPhone,
    string? OperatorPhone);

public sealed record RouteDto(Guid Id, string Name, Guid VehicleId, List<RouteStopDto>? Stops);

public sealed record RouteStopDto(
    Guid Id,
    Guid RouteId,
    int Sequence,
    string SettlementName,
    string RegionCode,
    double Latitude,
    double Longitude,
    DateTimeOffset PlannedArrivalUtc,
    string? PhotoDataUrl = null,
    DateTimeOffset? ArrivedAtUtc = null,
    bool Skipped = false);

public sealed record CreateRouteRequest(string Name, Guid VehicleId);

public sealed record UpdateRouteRequest(string? Name, Guid? VehicleId);

public sealed record ReplaceStopsRequest(List<ReplaceStopItem> Stops);

public sealed record ReplaceStopItem(
    Guid? Id,
    int Sequence,
    string SettlementName,
    string? RegionCode,
    double Latitude,
    double Longitude,
    DateTimeOffset? PlannedArrivalUtc,
    string? PhotoDataUrl = null,
    bool? ClearPhoto = null);

public sealed record CreateStopRequest(
    int Sequence,
    string SettlementName,
    string RegionCode,
    double Latitude,
    double Longitude,
    DateTimeOffset PlannedArrivalUtc);

public sealed record ChangeScheduleRequest(
    Guid RouteId,
    Guid StopId,
    DateTimeOffset NewArrivalUtc,
    string Reason);

public sealed record EtaDto(
    Guid Id,
    Guid VehicleId,
    Guid RouteId,
    Guid StopId,
    string SettlementName,
    DateTimeOffset EstimatedArrivalUtc,
    int MinutesUntilArrival,
    DateTimeOffset CalculatedAtUtc,
    double? DistanceKm = null);

public sealed record DriverNoteDto(
    Guid Id,
    Guid VehicleId,
    string Body,
    double Latitude,
    double Longitude,
    DateTimeOffset CreatedAtUtc);

public sealed record PostDriverNoteRequest(
    Guid VehicleId,
    string Body,
    double? Latitude,
    double? Longitude);

public sealed record CoverageDto(
    Guid Id,
    Guid VehicleId,
    Guid StopId,
    string SettlementName,
    string RegionCode,
    DateTimeOffset ArrivedAtUtc,
    bool WithinScheduledWindow);

public sealed record CoverageVisitRequest(
    Guid VehicleId,
    Guid StopId,
    DateTimeOffset? ArrivedAtUtc,
    bool WithinScheduledWindow);

public sealed record DeviceRegistrationRequest(
    Guid? UserId,
    string DeviceToken,
    string Platform,
    string SettlementName);

public sealed record NotificationDto(
    Guid Id,
    string Title,
    string Body,
    string SettlementName,
    DateTimeOffset SentAtUtc,
    int RecipientCount);

public sealed record PresenceReportDto(
    Guid Id,
    Guid StopId,
    string SettlementName,
    string Kind,
    string? DeviceToken,
    DateTimeOffset ReportedAtUtc);

public sealed record CaseSummaryDto(
    Guid Id,
    string SettlementKey,
    string SettlementName,
    Guid? VehicleId,
    string Status,
    int ReportCount,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ClosedAtUtc);

public sealed record CaseDetailDto(
    Guid Id,
    string SettlementKey,
    string SettlementName,
    Guid? VehicleId,
    string Status,
    int ReportCount,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    List<CaseEventDto> Events);

public sealed record CaseEventDto(
    Guid Id,
    Guid CaseId,
    string Kind,
    string Body,
    string? AuthorName,
    string? AuthorEmail,
    Guid? StopId,
    string? StopLabel,
    string? FromStatus,
    string? ToStatus,
    DateTimeOffset CreatedAtUtc);

public sealed record SnapshotDto(
    DateTimeOffset? SyncedAtUtc,
    List<VehicleDto> Vehicles,
    List<RouteDto> Routes,
    List<EtaDto> Eta,
    List<DriverNoteDto>? DriverNotes = null);

public sealed record DispatchSnapshotDto(
    DateTimeOffset SyncedAtUtc,
    List<VehicleDto> Vehicles,
    List<CaseSummaryDto> Cases,
    List<EtaDto> Eta,
    List<RouteDto> Routes,
    List<DriverNoteDto> DriverNotes);
