using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Avtomagazin.Admin;

public static class AppRenderMode
{
    public static readonly IComponentRenderMode Interactive =
        new InteractiveServerRenderMode(prerender: false);
}
