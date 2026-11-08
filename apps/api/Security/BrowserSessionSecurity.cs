using Microsoft.AspNetCore.Antiforgery;

namespace Carpoolr.Api.Security;

public static class BrowserSessionSecurity
{
    public static IApplicationBuilder UseBrowserPublicOrigin(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/auth"))
            {
                // Also covers callbacks completed inside authentication middleware.
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";
            }

            var settings = context.RequestServices.GetRequiredService<BrowserAuthenticationSettings>();
            if (settings.Enabled)
            {
                // Browser mode has an explicitly validated HTTPS public origin.
                // TLS terminates at ingress; nginx forwards over HTTP and strips /api.
                // Use that configured scheme, never an untrusted forwarded header.
                context.Request.Scheme = Uri.UriSchemeHttps;
            }

            await next(context);
        });

    public static IApplicationBuilder UseBrowserSessionSecurity(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var settings = context.RequestServices.GetRequiredService<BrowserAuthenticationSettings>();
            if (settings.Enabled && context.User.Identity?.IsAuthenticated == true &&
                !BrowserAuthentication.UsesAuthorizationHeader(context))
            {
                context.Response.Headers.CacheControl = "no-store";
                if (!HttpMethods.IsGet(context.Request.Method) &&
                    !HttpMethods.IsHead(context.Request.Method) &&
                    !HttpMethods.IsOptions(context.Request.Method))
                {
                    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                    try
                    {
                        await antiforgery.ValidateRequestAsync(context);
                    }
                    catch (AntiforgeryValidationException)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(new { error = "invalid_csrf_token" });
                        return;
                    }
                }
            }

            await next(context);
        });
}
