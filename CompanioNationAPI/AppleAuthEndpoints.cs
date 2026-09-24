using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using CompanioNation.Shared;

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
    /// Domain-separation label for the encrypted Apple email handoff. Also referenced
    /// by Database.LoginWithAppleAsync so both sides always agree.
    /// </summary>
    public const string AppleEmailHandoffPurpose = "APPLE_LOGIN_HANDOFF|v1";

    /// <summary>
    /// Registers the <c>/auth/apple/callback</c> POST endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapAppleAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/apple/callback", async (HttpContext ctx) =>
        {
            // Apple's form_post is a cross-site POST that can arrive without a form
            // content type (probes, scanners). Never parse a body that isn't a form.
            if (!ctx.Request.HasFormContentType)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var form = await ctx.Request.ReadFormAsync();

            var code = form["code"].ToString();
            var state = form["state"].ToString();
            var userJson = form["user"].ToString(); // Apple sends user info only on first authorization

            // Apple's form_post error response (e.g. access_denied when the user cancels,
            // or invalid_request from Apple-side problems) carries NO code and was
            // previously dropped on the floor here — the Blazor page then fell through to
            // its bare-callback path and the failure was invisible everywhere. Forward the
            // error so the callback page can show and log it.
            var error = form["error"].ToString();
            var errorDescription = form["error_description"].ToString();

            var firstName = "";
            var lastName = "";
            var email = "";

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

                    // On first authorization Apple also puts the user's email here.
                    // It is forgeable, so it is treated as UNTRUSTED downstream and
                    // transported as an encrypted blob (privacy, not authentication).
                    if (doc.RootElement.TryGetProperty("email", out var em) && em.ValueKind == JsonValueKind.String)
                        email = em.GetString() ?? "";
                }
                catch
                {
                    // user JSON parsing failure is non-fatal; name and email are optional
                }
            }

            // Encrypt the (untrusted) email so it can ride the 302 redirect without
            // leaking PII into the URL. Bound to the one-time code so a captured blob
            // cannot be paired with another flow. The login path will treat it as
            // create-only and never use it to claim an existing account.
            string emailHandoff = "";
            if (!string.IsNullOrWhiteSpace(email))
            {
                string secret = Environment.GetEnvironmentVariable(SecureUrlPayload.SecretEnvironmentVariable) ?? "";
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    try
                    {
                        emailHandoff = SecureUrlPayload.Create(email, AppleEmailHandoffPurpose, secret, associatedData: code);
                    }
                    catch
                    {
                        // A crypto/config failure must never 500 Apple's POST — the
                        // signed id_token path still works without this optional handoff.
                        emailHandoff = "";
                    }
                }
            }

            // Redirect to the Blazor WASM callback page with query parameters. The email
            // travels only as ciphertext; code/state/name remain as before.
            var redirectUrl = $"/auth/apple/complete?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}&firstName={Uri.EscapeDataString(firstName)}&lastName={Uri.EscapeDataString(lastName)}";
            if (!string.IsNullOrWhiteSpace(emailHandoff))
                redirectUrl += $"&e={Uri.EscapeDataString(emailHandoff)}";
            if (!string.IsNullOrWhiteSpace(error))
            {
                redirectUrl += $"&error={Uri.EscapeDataString(error)}";
                if (!string.IsNullOrWhiteSpace(errorDescription))
                    redirectUrl += $"&error_description={Uri.EscapeDataString(errorDescription)}";
            }
            ctx.Response.Redirect(redirectUrl);
        });

        return app;
    }
}
