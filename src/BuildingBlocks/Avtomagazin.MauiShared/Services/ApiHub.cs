using Avtomagazin.ApiClient;

namespace Avtomagazin.MauiShared;

public sealed class ApiHub
{
    public AvtomagazinClient Client { get; }

    public ApiHub(Session session)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri(session.Gateway.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(12)
        };
        Client = new AvtomagazinClient(http, session);
    }
}
