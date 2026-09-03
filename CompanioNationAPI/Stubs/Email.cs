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
