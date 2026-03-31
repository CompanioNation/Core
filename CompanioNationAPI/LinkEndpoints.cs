using CompanioNation.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;

namespace CompanioNationAPI;

/// <summary>
/// Server-rendered LINK confirmation/rejection pages.
/// Lives in the open-source Core repo so the feature is fully transparent.
/// Called from Services Program.cs via <c>app.MapLinkEndpoints()</c>.
/// </summary>
public static class LinkEndpoints
{
    /// <summary>
    /// Registers the <c>/s/confirm-link/{code}</c> and <c>/s/reject-link/{code}</c> endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/s/confirm-link/{code}", async (string code, HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            var db = ctx.RequestServices.GetRequiredService<Database>();
            var result = await db.ConfirmLinkAsync(code);

            if (result.IsSuccess)
            {
                var (loginToken, initiatorName) = result.Data;
                var encodedName = WebUtility.HtmlEncode(initiatorName);
                await ctx.Response.WriteAsync(RenderLinkStatusPage(
                    title: "LINK Confirmed — CompanioNation",
                    heading: $"✅ You're now LINKed with {encodedName}!",
                    headingColor: "#4caf50",
                    message: "You've been automatically logged in. Head over to your LINK page to see your connections and upload photos for extra Karma!",
                    ctaText: "Go to your LINKs",
                    ctaUrl: "/Link",
                    loginToken: loginToken));
            }
            else if (result.ErrorCode == ErrorCodes.LinkAlreadyExists)
            {
                await ctx.Response.WriteAsync(RenderLinkStatusPage(
                    title: "Already Confirmed — CompanioNation",
                    heading: "Already Confirmed",
                    headingColor: "#1565c0",
                    message: "This LINK has already been confirmed. You're all set!",
                    ctaText: "Go to your LINKs",
                    ctaUrl: "/Link"));
            }
            else
            {
                await ctx.Response.WriteAsync(RenderLinkStatusPage(
                    title: "Invalid or Expired — CompanioNation",
                    heading: "Invalid or Expired",
                    headingColor: "#d32f2f",
                    message: "This LINK invitation has expired or is invalid. Ask your friend to send a new one!",
                    ctaText: "Go to CompanioNation",
                    ctaUrl: "/"));
            }
        });

        app.MapGet("/s/reject-link/{code}", async (string code, HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            var db = ctx.RequestServices.GetRequiredService<Database>();
            var result = await db.RejectLinkAsync(code);

            if (result.IsSuccess)
            {
                await ctx.Response.WriteAsync(RenderLinkStatusPage(
                    title: "Reported — CompanioNation",
                    heading: "Thank You",
                    headingColor: "#333",
                    message: "This has been reported. We take these reports seriously and will review the activity.",
                    ctaText: "Go to CompanioNation",
                    ctaUrl: "/"));
            }
            else
            {
                await ctx.Response.WriteAsync(RenderLinkStatusPage(
                    title: "Invalid — CompanioNation",
                    heading: "Invalid",
                    headingColor: "#d32f2f",
                    message: "This LINK invitation is invalid, expired, or was already handled.",
                    ctaText: "Go to CompanioNation",
                    ctaUrl: "/"));
            }
        });

        return app;
    }

    private static string RenderLinkStatusPage(string title, string heading, string headingColor, string message, string ctaText, string ctaUrl, string? loginToken = null)
    {
        var sb = new StringBuilder();
        sb.Append("""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
""");
        sb.Append($"  <title>{WebUtility.HtmlEncode(title)}</title>");
        sb.Append("""

  <style>
    body{font-family:Arial,Helvetica,sans-serif;max-width:600px;margin:0 auto;padding:24px;text-align:center;line-height:1.6;background:#fafafa;}
    .logo{margin-bottom:8px;}
    .logo img{height:120px;width:120px;}
    .card{border:1px solid #e0e0e0;border-radius:12px;padding:32px 24px;margin-top:16px;box-shadow:0 2px 8px rgba(0,0,0,0.08);background:#fff;}
    .card p{color:#555;font-size:1.05em;}
    .cta{display:inline-block;margin-top:16px;padding:12px 32px;border-radius:8px;font-size:1em;font-weight:700;text-decoration:none;color:#fff;background:#4caf50;transition:background 0.2s;}
    .cta:hover{background:#388e3c;text-decoration:none;}
    .home-link{display:block;margin-top:14px;font-size:0.9em;color:#1565c0;text-decoration:none;}
    .home-link:hover{text-decoration:underline;}
  </style>
</head>
<body>
  <div class="logo"><img src="/images/CompanioNita.png" alt="CompanioNation" /></div>
  <div class="card">
""");
        sb.Append($"    <h1 style=\"color:{headingColor}\">{heading}</h1>");
        sb.Append($"    <p>{WebUtility.HtmlEncode(message)}</p>");
        sb.Append($"    <a class=\"cta\" href=\"{ctaUrl}\">{WebUtility.HtmlEncode(ctaText)}</a>");
        sb.Append("    <a class=\"home-link\" href=\"/\">Return to CompanioNation home</a>");
        sb.Append("  </div>");

        if (loginToken != null)
        {
            sb.Append($"<script>try {{ localStorage.setItem('loginGuid', '{loginToken}'); }} catch(e) {{ }}</script>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }
}
