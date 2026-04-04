using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;

namespace CompanioNationAPI;

/// <summary>
/// Server-side endpoints for Apple Sign In.
/// Apple uses response_mode=form_post, so the authorization code arrives via HTTP POST.
/// Blazor WASM cannot receive POST requests, so this endpoint captures the form data
/// and redirects to the Blazor callback page with query parameters.
/// Called from Services Program.cs via <c>app.MapAppleAuthEndpoints()</c>.
/// </summary>
public static class AppleAuthEndpoints
{
    /// <summary>
    /// Registers the <c>/auth/apple/callback</c> POST endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapAppleAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/apple/callback", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();

            var code = form["code"].ToString();
            var state = form["state"].ToString();
            var userJson = form["user"].ToString(); // Apple sends user info only on first authorization

            var firstName = "";
            var lastName = "";

            if (!string.IsNullOrEmpty(userJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(userJson);
                    if (doc.RootElement.TryGetProperty("name", out var nameEl))
                    {
                        if (nameEl.TryGetProperty("firstName", out var fn))
                            firstName = fn.GetString() ?? "";
                        if (nameEl.TryGetProperty("lastName", out var ln))
                            lastName = ln.GetString() ?? "";
                    }
                }
                catch
                {
                    // user JSON parsing failure is non-fatal; name is optional
                }
            }

            // Redirect to the Blazor WASM callback page with query parameters
            var redirectUrl = $"/auth/apple/complete?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}&firstName={Uri.EscapeDataString(firstName)}&lastName={Uri.EscapeDataString(lastName)}";
            ctx.Response.Redirect(redirectUrl);
        });

        return app;
    }
}
