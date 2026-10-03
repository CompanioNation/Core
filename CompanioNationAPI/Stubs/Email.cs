using CompanioNation.Shared;

namespace CompanioNationAPI;

/// <summary>
/// Simple email facade that can be swapped for the real implementation in CompanioNationServices.
/// </summary>
public static class Email
{
    public static IEmailSender Implementation { get; set; } = new DefaultEmailSender();

    public static Task<bool> SendEmailAsync(string to, string subject, string textBody, string htmlBody)
    {
        return Implementation.SendEmailAsync(to, subject, textBody, htmlBody);
    }

    public static Task<bool> SendTextEmailAsync(string to, string subject, string textBody)
    {
        return Implementation.SendTextEmailAsync(to, subject, textBody);
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
/// pipeline). Never pass plain text in the HTML parameter.
/// </summary>
public interface IEmailSender
{
    Task<bool> SendEmailAsync(string to, string subject, string textBody, string htmlBody);
    Task<bool> SendTextEmailAsync(string to, string subject, string textBody);
}

internal sealed class DefaultEmailSender : IEmailSender
{
    public Task<bool> SendEmailAsync(string to, string subject, string textBody, string htmlBody)
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
