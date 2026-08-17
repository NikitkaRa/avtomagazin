namespace Avtomagazin.ApiClient;

public static class ApiErrors
{
    public static string Friendly(Exception ex)
    {
        if (ex is SessionExpiredException)
        {
            return "Сессия истекла — войдите снова";
        }

        return Friendly(ex.GetBaseException().Message);
    }

    public static string Friendly(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Ошибка запроса";
        }

        if (message.Contains("502", StringComparison.Ordinal)
            || message.Contains("Bad Gateway", StringComparison.OrdinalIgnoreCase)
            || message.Contains("временно недоступен", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Service Unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return "Сервер временно недоступен";
        }

        if (message.Contains("401", StringComparison.Ordinal)
            || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return "Сессия истекла — войдите снова";
        }

        if (message.Contains("Connection", StringComparison.OrdinalIgnoreCase)
            || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Name or service", StringComparison.OrdinalIgnoreCase)
            || message.Contains("refused", StringComparison.OrdinalIgnoreCase))
        {
            return "Нет ответа от сервера";
        }

        return message.Length > 140 ? message[..140] + "…" : message;
    }

    public static string FriendlyLogin(Exception ex)
    {
        var msg = Friendly(ex);
        if (msg is "Сервер временно недоступен" or "Нет ответа от сервера")
        {
            return "Нет связи с сервером. Проверьте, что API запущен.";
        }

        return msg;
    }
}
