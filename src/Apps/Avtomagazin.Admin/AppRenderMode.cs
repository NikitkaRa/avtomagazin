using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Avtomagazin.Admin;

public static class AppRenderMode
{
    // Prerender so login/shell CSS paints before the circuit connects.
    public static readonly IComponentRenderMode Interactive =
        new InteractiveServerRenderMode(prerender: true);
}
