namespace Avtomagazin.ApiClient;

public static class AvtomagazinEnvironments
{
    public const string Development = "Development";
    public const string Staging = "Staging";
    public const string Production = "Production";

    public const string StagingGateway = "https://api.staging.avtomagazin.by";
    public const string ProductionGateway = "https://api.avtomagazin.by";
    public const string LocalGateway = "http://127.0.0.1:5100";

    public static string GatewayUrl(string environment)
        => environment switch
        {
            Staging => StagingGateway,
            Production => ProductionGateway,
            _ => LocalGateway
        };
}
