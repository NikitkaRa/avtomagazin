using Avtomagazin.ApiClient;

namespace Avtomagazin.Mobile;

public sealed class ApiHub(Session session)
{
    public AvtomagazinClient Client
    {
        get
        {
            var baseUrl = session.Gateway.TrimEnd('/') + "/";
            var http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(12)
            };
            return new AvtomagazinClient(http, session);
        }
    }
}
