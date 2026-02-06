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
}

public interface IEmailSender
{
    Task<bool> SendEmailAsync(string to, string subject, string textBody, string htmlBody);
}

internal sealed class DefaultEmailSender : IEmailSender
{
    public Task<bool> SendEmailAsync(string to, string subject, string textBody, string htmlBody)
    {
        Console.WriteLine($"[Email stub] To: {to}, Subject: {subject}");
        return Task.FromResult(true);
    }
}
