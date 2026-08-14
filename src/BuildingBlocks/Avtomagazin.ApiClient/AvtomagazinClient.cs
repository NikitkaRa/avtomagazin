using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Avtomagazin.ApiClient;

public interface IAccessTokenAccessor
{
    string? AccessToken { get; }
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

    public async Task<LoginResponse?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync("identity/api/auth/login", new { email, password }, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, ct);
    }

    public Task<List<VehicleDto>> GetVehiclesAsync(CancellationToken ct = default)
        => GetListAsync<VehicleDto>("fleet/api/vehicles", ct);

    public async Task<VehicleDto?> CreateVehicleAsync(CreateVehicleRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync("fleet/api/vehicles", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<VehicleDto>(JsonOptions, ct);
    }

    public async Task SetVehicleActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PatchAsJsonAsync($"fleet/api/vehicles/{id}/active", new { isActive }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task IngestPositionAsync(Guid vehicleId, double latitude, double longitude, double? speedKmh = null, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync(
            $"fleet/api/vehicles/{vehicleId}/positions",
            new { latitude, longitude, speedKmh, source = "driver-app" },
            ct);
        response.EnsureSuccessStatusCode();
    }

    public Task<List<RouteDto>> GetRoutesAsync(CancellationToken ct = default)
        => GetListAsync<RouteDto>("routing/api/routes", ct);

    public async Task<RouteDto?> CreateRouteAsync(CreateRouteRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync("routing/api/routes", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RouteDto>(JsonOptions, ct);
    }

    public async Task AddStopAsync(Guid routeId, CreateStopRequest request, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync($"routing/api/routes/{routeId}/stops", request, ct);
        response.EnsureSuccessStatusCode();
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

    public async Task ArriveAtStopAsync(Guid stopId, Guid vehicleId, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync($"routing/api/stops/{stopId}/arrived", new { vehicleId }, ct);
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

    public async Task AddFavoriteStopAsync(string deviceToken, Guid stopId, string settlementName, string platform = "web", CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.PostAsJsonAsync(
            "notifications/api/favorites",
            new { deviceToken, stopId, settlementName, platform },
            ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveFavoriteStopAsync(string deviceToken, Guid stopId, CancellationToken ct = default)
    {
        ApplyAuth();
        var response = await http.DeleteAsync(
            $"notifications/api/favorites?deviceToken={Uri.EscapeDataString(deviceToken)}&stopId={stopId}",
            ct);
        response.EnsureSuccessStatusCode();
    }

    public Task<List<PresenceReportDto>> GetPresenceReportsAsync(CancellationToken ct = default)
        => GetListAsync<PresenceReportDto>("routing/api/presence-reports", ct);

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
        var vehicles = await GetVehiclesAsync(ct);
        var routes = await GetRoutesAsync(ct);
        var eta = await GetEtaAsync(ct: ct);
        return new SnapshotDto(DateTimeOffset.UtcNow, vehicles, routes, eta);
    }

    private async Task<List<T>> GetListAsync<T>(string path, CancellationToken ct)
    {
        ApplyAuth();
        var items = await http.GetFromJsonAsync<List<T>>(path, JsonOptions, ct);
        return items ?? [];
    }
}

public sealed record LoginResponse(
    string AccessToken,
    string Role,
    Guid UserId,
    string Email,
    string? Name,
    Guid? VehicleId);

public sealed record VehicleDto(
    Guid Id,
    string PlateNumber,
    string OperatorName,
    bool IsActive,
    double? LastLatitude,
    double? LastLongitude,
    DateTimeOffset? LastSeenAtUtc,
    string? LastSource);

public sealed record CreateVehicleRequest(string PlateNumber, string OperatorName);

public sealed record RouteDto(Guid Id, string Name, Guid VehicleId, List<RouteStopDto> Stops);

public sealed record RouteStopDto(
    Guid Id,
    Guid RouteId,
    int Sequence,
    string SettlementName,
    string RegionCode,
    double Latitude,
    double Longitude,
    DateTimeOffset PlannedArrivalUtc);

public sealed record CreateRouteRequest(string Name, Guid VehicleId);

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
    DateTimeOffset CalculatedAtUtc);

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
    string DeviceToken,
    DateTimeOffset ReportedAtUtc);

public sealed record SnapshotDto(
    DateTimeOffset? SyncedAtUtc,
    List<VehicleDto> Vehicles,
    List<RouteDto> Routes,
    List<EtaDto> Eta);
