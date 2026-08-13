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
    public virtual Task<ResponseWrapper<string>> AskCompanioNitaAsync(string loginToken, string message)
    {
        //return Task.FromResult(ResponseWrapper<string>.Fail(ErrorCodes.SubscriptionRequired, "CompanioNita service is not available. This is a stub implementation."));

        if (string.IsNullOrWhiteSpace(message)) message = "(no question provided)";
        return Task.FromResult(ResponseWrapper<string>.Success(
            $"CompanioNita (stub) received: {message}"));
    }

    /// <summary>
    /// Streams the CompanioNita response token-by-token. Override in derived classes
    /// for real AI provider streaming. The stub yields the full response as a single chunk.
    /// </summary>
    public virtual async IAsyncEnumerable<string> StreamAskCompanioNitaAsync(string loginToken, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) message = "(no question provided)";
        yield return $"CompanioNita (stub) received: {message}";
    }

    public virtual Task<ResponseWrapper<string>> AskCompanioNitaAboutConversation(string loginToken, int userId)
    {
        string summary = "CompanioNita can give advice about a conversation";
        return Task.FromResult(ResponseWrapper<string>.Success(
            $"CompanioNita (stub) summary: {summary}"));
    }

    public virtual Task<ResponseWrapper<bool>> DetectFaceAsync(byte[] imageData)
    {
        // Stub always succeeds so that flows depending on this continue in development.
        return Task.FromResult(ResponseWrapper<bool>.Success(true));
    }

    public virtual Task<ResponseWrapper<string>> GenerateDailyAdviceOutlineAsync(string previousOutlines, string recentMessages)
    {
        return Task.FromResult(ResponseWrapper<string>.Success(
            "Placeholder outline: headline; hook; two sections; takeaway; closing."));
    }

    public virtual Task<ResponseWrapper<string>> GenerateDailyAdviceFromOutlineAsync(string outline, string languageCode)
    {
        return Task.FromResult(ResponseWrapper<string>.Success(
            $"CompanioNita (stub) daily advice from outline in {languageCode}: {outline}"));
    }
}
