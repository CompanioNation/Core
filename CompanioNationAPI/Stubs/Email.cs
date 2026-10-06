using CompanioNation.Shared;

namespace CompanioNationAPI;

/// <summary>
/// Simple email facade that can be swapped for the real implementation in CompanioNationServices.
/// Centralizes the unsubscribe contract for USER-FACING mail: opt-out gating, the Gmail
/// one-click <c>List-Unsubscribe</c> headers, and the visible footer unsubscribe link.
/// ADMIN/operational alerts go through <see cref="SendAdminEmailAsync"/> and deliberately
/// carry NO unsubscribe machinery; they default to the configured admin mailbox.
/// </summary>
public static class Email
{
    public static IEmailSender Implementation { get; set; } = new DefaultEmailSender();

    /// <summary>
    /// Shared secret used to mint/verify unsubscribe tokens (see
    /// <see cref="SecureUrlPayload.SecretEnvironmentVariable"/>). Assigned at startup by the
    /// host; when empty, the unsubscribe header/footer are omitted but mail still sends.
    /// </summary>
    public static string LinkSecret { get; set; } = string.Empty;

    /// <summary>Environment variable holding the operational admin mailbox for admin alerts.</summary>
    public const string AdminEmailEnvironmentVariable = "COMPANIONATION_ADMIN_EMAIL";

    /// <summary>Fallback admin mailbox used when the environment variable is unset.</summary>
    public const string DefaultAdminEmail = "errors@companionation.com";

    /// <summary>
    /// The configured admin mailbox for admin/operational alerts: the value of
    /// <see cref="AdminEmailEnvironmentVariable"/>, or <see cref="DefaultAdminEmail"/> when
    /// unset. The single source of truth for where admin mail goes.
    /// </summary>
    public static string AdminAddress
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable(AdminEmailEnvironmentVariable);
            return string.IsNullOrWhiteSpace(configured) ? DefaultAdminEmail : configured.Trim();
        }
    }

    /// <summary>
    /// Sends an ADMIN/operational alert (error reports, maintenance summaries, fallback
    /// notices, feedback) with NO unsubscribe footer/header and no opt-out gating. Defaults
    /// to the configured <see cref="AdminAddress"/>; pass <paramref name="to"/> to target a
    /// different operational mailbox (e.g. feedback@).
    /// </summary>
    public static Task<bool> SendAdminEmailAsync(string subject, string textBody, string htmlBody, string? to = null)
        => Implementation.SendEmailAsync(string.IsNullOrWhiteSpace(to) ? AdminAddress : to.Trim(), subject, textBody, htmlBody);

    /// <summary>
    /// Sends an OPTIONAL (non-essential) USER-FACING email with an HTML and plain-text body.
    /// Honors the recipient's opt-out flag, which the CALLER supplies from the already-loaded
    /// <see cref="UserDetails.EmailsEnabled"/> - never by re-querying the database. Attaches
    /// the one-click <c>List-Unsubscribe</c> headers and footer link.
    /// </summary>
    public static Task<bool> SendEmailAsync(string to, string subject, string textBody, string htmlBody, bool emailsEnabled = true)
    {
        // Recipient unsubscribed: skip. Report success so callers never surface a
        // failure for a user who asked not to be emailed.
        if (!emailsEnabled) return Task.FromResult(true);

        return SendUserFacingAsync(to, subject, textBody, htmlBody);
    }

    /// <summary>
    /// Sends a MANDATORY USER-FACING email (signup verification, welcome, password reset,
    /// email change, and anything the user explicitly requested). This path ALWAYS sends and
    /// NEVER consults the opt-out flag - unsubscribing must never lock a user out of
    /// account-critical mail. The unsubscribe header/footer are still attached so the
    /// user can opt out of future OPTIONAL mail.
    /// </summary>
    public static Task<bool> SendMandatoryEmailAsync(string to, string subject, string textBody, string htmlBody)
        => SendUserFacingAsync(to, subject, textBody, htmlBody);

    private static Task<bool> SendUserFacingAsync(string to, string subject, string textBody, string htmlBody)
    {
        string? token = EmailUnsubscribe.CreateToken(to, LinkSecret);
        return Implementation.SendEmailAsync(
            to,
            subject,
            AppendTextFooter(textBody, token),
            AppendHtmlFooter(htmlBody, token),
            BuildUnsubscribeHeaders(token));
    }

    public static Task<bool> SendTextEmailAsync(string to, string subject, string textBody)
    {
        return Implementation.SendTextEmailAsync(to, subject, textBody);
    }

    private static string AppendTextFooter(string textBody, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return textBody;

        string url = EmailUnsubscribe.FooterUrl(Util.SiteBaseUrl, token);
        string footer = $"\r\n\r\n---\r\nUnsubscribe: {url}\r\n";
        return (textBody ?? string.Empty) + footer;
    }

    private static string AppendHtmlFooter(string htmlBody, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return htmlBody;

        string url = EmailUnsubscribe.FooterUrl(Util.SiteBaseUrl, token);
        string footer =
            "<hr style=\"border:0;border-top:1px solid #dddddd;margin:20px 0;\" />" +
            "<p style=\"font-size:12px;color:#999999;text-align:center;\">" +
            $"<a href=\"{url}\" style=\"color:#999999;\">Unsubscribe</a> from CompanioNation&#8482; email." +
            "</p>";
        return (htmlBody ?? string.Empty) + footer;
    }

    private static IReadOnlyDictionary<string, string>? BuildUnsubscribeHeaders(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        string oneClickUrl = EmailUnsubscribe.OneClickUrl(Util.SiteBaseUrl, token);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["List-Unsubscribe"] = $"<{oneClickUrl}>",
            ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click"
        };
    }
}

/// <summary>
/// Facade for recording the DELIVERED/FAILED outcome of an email send, mirroring the
/// <see cref="Email"/> facade pattern. The real implementation lives in
/// CompanioNationServices and persists state per recipient; the default is a no-op so
/// the API project (and tests) never require the delivery pipeline.
/// </summary>
public static class EmailDeliveryTracker
{
    public static IEmailDeliveryTracker Implementation { get; set; } = new NoOpEmailDeliveryTracker();

    /// <summary>
    /// Records the delivery outcome for a recipient. Implementations must be best-effort:
    /// recording a delivery result must never throw or affect the send itself.
    /// </summary>
    public static Task RecordAsync(string toAddress, EmailDeliveryState state, string? error) =>
        Implementation.RecordAsync(toAddress, state, error);
}

/// <summary>
/// Records per-recipient email delivery outcomes. State is surfaced on
/// <see cref="UserDetails.EmailDeliveryState"/> so the client can offer a recovery path
/// (e.g. "change your email") when mail cannot be delivered.
/// </summary>
public interface IEmailDeliveryTracker
{
    Task RecordAsync(string toAddress, EmailDeliveryState state, string? error);
}

/// <summary>
/// Default tracker used when no real implementation is wired (API-only host, tests,
/// or before startup wiring runs). Deliberately does nothing.
/// </summary>
internal sealed class NoOpEmailDeliveryTracker : IEmailDeliveryTracker
{
    public Task RecordAsync(string toAddress, EmailDeliveryState state, string? error) => Task.CompletedTask;
}

/// <summary>
/// Email delivery contract. Callers decide the content format explicitly:
/// <see cref="SendEmailAsync"/> is for messages with a real HTML body, and
/// <see cref="SendTextEmailAsync"/> is for plain-text-only messages (e.g. the error
/// pipeline). Never pass plain text in the HTML parameter. The optional
/// <paramref name="headers"/> dictionary carries custom email headers (e.g. the
/// Gmail one-click <c>List-Unsubscribe</c> pair).
/// </summary>
public interface IEmailSender
{
    Task<bool> SendEmailAsync(string to, string subject, string textBody, string htmlBody, IReadOnlyDictionary<string, string>? headers = null);
    Task<bool> SendTextEmailAsync(string to, string subject, string textBody);
}

internal sealed class DefaultEmailSender : IEmailSender
{
    public Task<bool> SendEmailAsync(string to, string subject, string textBody, string htmlBody, IReadOnlyDictionary<string, string>? headers = null)
    {
        Console.WriteLine($"[Email stub] To: {to}, Subject: {subject}");
        return Task.FromResult(true);
    }

    public Task<bool> SendTextEmailAsync(string to, string subject, string textBody)
    {
        Console.WriteLine($"[Email stub] To: {to}, Subject: {subject}");
        return Task.FromResult(true);
    }
}
