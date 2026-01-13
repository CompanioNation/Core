using System.Linq;
using CompanioNation.Shared;

namespace CompanioNationAPI;

/// <summary>
/// Minimal default implementation to keep the API running when the real CompanioNita
/// implementation from CompanioNationServices is not available.
/// Override these virtual methods in a derived class inside CompanioNationServices
/// and register that derived type with DI.
/// </summary>
public class CompanioNita
{
    public virtual Task<string> AskCompanioNitaAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) message = "(no question provided)";
        return Task.FromResult($"CompanioNita (stub) received: {message}");
    }

    public virtual Task<string> AskCompanioNitaAboutConversation(IEnumerable<UserMessage> messages)
    {
        string summary = messages?.FirstOrDefault()?.MessageText ?? "No prior conversation available.";
        return Task.FromResult($"CompanioNita (stub) summary: {summary}");
    }

    public virtual Task<string> AskCompanioNitaToIntroduce(UserDetails requester, UserConversation other)
    {
        return Task.FromResult("Hi there! I'm a placeholder CompanioNita. Let's start chatting.");
    }

    public virtual Task<bool> DetectFaceAsync(byte[] imageData)
    {
        // Stub always succeeds so that flows depending on this continue in development.
        return Task.FromResult(true);
    }

    public virtual Task<string> GenerateDailyAdviceAsync(string previousDailyAdvice, string recentMessages)
    {
        return Task.FromResult("This is placeholder daily advice from CompanioNita.");
    }
}
