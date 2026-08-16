namespace Avtomagazin.ApiClient;

public sealed class SessionExpiredException() : InvalidOperationException("Сессия истекла — войдите снова.");
