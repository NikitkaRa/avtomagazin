namespace Avtomagazin.Routing.Api.Endpoints;

public static class RoutingEndpoints
{
    public static WebApplication MapRoutingApi(this WebApplication app)
    {
        app.MapRouteEndpoints();
        app.MapDriverNoteEndpoints();
        app.MapCoverageEndpoints();
        app.MapCaseEndpoints();
        return app;
    }
}
