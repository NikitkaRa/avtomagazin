using Avtomagazin.ApiClient;

namespace Avtomagazin.UnitTests.ApiClient;

public class ApiErrorsTests
{
    [Fact]
    public void Friendly_maps_gateway_and_session()
    {
        Assert.Equal("Сервер временно недоступен", ApiErrors.Friendly("502 Bad Gateway"));
        Assert.Equal("Сервер временно недоступен", ApiErrors.Friendly(new InvalidOperationException("Сервис временно недоступен. Попробуйте ещё раз.")));
        Assert.Equal("Сессия истекла — войдите снова", ApiErrors.Friendly(new SessionExpiredException()));
        Assert.Equal("Нет ответа от сервера", ApiErrors.Friendly("Connection refused"));
        Assert.Equal("Нет связи с сервером. Проверьте, что API запущен.", ApiErrors.FriendlyLogin(new InvalidOperationException("502")));
    }
}
