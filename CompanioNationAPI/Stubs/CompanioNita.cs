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
    public virtual Task<ResponseWrapper<string>> AskCompanioNitaAsync(string loginToken, int threadId, string message)
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
    public virtual async IAsyncEnumerable<string> StreamAskCompanioNitaAsync(string loginToken, int threadId, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) message = "(no question provided)";
        yield return $"CompanioNita (stub) received: {message}";
    }

    /// <summary>
    /// Streams CompanioNita's insight into a conversation. Override in derived classes
    /// for real AI provider streaming; the stub yields the full placeholder as one chunk.
    /// </summary>
    public virtual async IAsyncEnumerable<string> StreamAskCompanioNitaAboutConversationAsync(string loginToken, int userId)
    {
        yield return "CompanioNita can give advice about a conversation";
    }

    public virtual Task<ResponseWrapper<bool>> DetectFaceAsync(byte[] imageData)
    {
        // Stub always succeeds so that flows depending on this continue in development.
        return Task.FromResult(ResponseWrapper<bool>.Success(true));
    }

    public virtual Task<ResponseWrapper<string>> GenerateDailyAdviceOutlineAsync(
        string previousOutlines, string recentMessages, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ResponseWrapper<string>.Success(
            "Placeholder outline: headline; hook; two sections; takeaway; closing."));
    }

    public virtual Task<ResponseWrapper<string>> GenerateDailyAdviceFromOutlineAsync(
        string outline, string languageCode, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ResponseWrapper<string>.Success(
            $"CompanioNita (stub) daily advice from outline in {languageCode}: {outline}"));
    }

    /// <summary>
    /// Sends a minimal ping to the AI provider to verify connectivity and warm the model
    /// endpoint before a batch of calls (used by the nightly maintenance job). Override in
    /// derived classes for real providers. The stub always succeeds so maintenance flows
    /// are unaffected in development builds without a live AI provider.
    /// </summary>
    public virtual Task<ResponseWrapper<bool>> WarmupAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ResponseWrapper<bool>.Success(true));
}
