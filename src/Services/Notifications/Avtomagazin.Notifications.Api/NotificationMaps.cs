using Avtomagazin.Contracts;
using Avtomagazin.Notifications.Api.Data;

namespace Avtomagazin.Notifications.Api;

internal static class NotificationMaps
{
    public static DeviceDto ToDto(this DeviceSubscription device) => new(
        device.Id,
        device.UserId,
        device.DeviceToken,
        device.Platform,
        device.SettlementName);

    public static FavoriteDto ToDto(this FavoriteStop favorite) => new(
        favorite.StopId,
        favorite.SettlementName,
        favorite.CreatedAtUtc);

    public static NotificationDto ToDto(this NotificationLog log) => new(
        log.Id,
        log.Title,
        log.Body,
        log.SettlementName,
        log.SentAtUtc,
        log.RecipientCount);
}
