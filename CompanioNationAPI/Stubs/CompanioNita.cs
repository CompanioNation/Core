using System.Linq;
using System.Runtime.CompilerServices;
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
    public virtual async IAsyncEnumerable<string> StreamAskCompanioNitaAsync(
        string loginToken, int threadId, string message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message)) message = "(no question provided)";
        yield return $"CompanioNita (stub) received: {message}";
    }

    /// <summary>
    /// Streams CompanioNita's insight into a conversation. Override in derived classes
    /// for real AI provider streaming; the stub yields the full placeholder as one chunk.
    /// </summary>
    public virtual async IAsyncEnumerable<string> StreamAskCompanioNitaAboutConversationAsync(
        string loginToken, int userId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
    /// Generates the English outline and returns it together with generation telemetry
    /// (provider, attempts, thinking fallback, timings) used by the nightly admin report.
    /// The stub default wraps the plain overload and reports no telemetry, so implementations
    /// outside CompanioNationServices keep working unchanged.
    /// </summary>
    public virtual async Task<ResponseWrapper<DailyAdviceGeneration>> GenerateDailyAdviceOutlineWithReportAsync(
        string previousOutlines, string recentMessages, CancellationToken cancellationToken = default)
    {
        ResponseWrapper<string> result = await GenerateDailyAdviceOutlineAsync(previousOutlines, recentMessages, cancellationToken);
        return result.IsSuccess
            ? ResponseWrapper<DailyAdviceGeneration>.Success(new DailyAdviceGeneration { Text = result.Data })
            : ResponseWrapper<DailyAdviceGeneration>.Fail(result.ErrorCode, result.Message,
                new DailyAdviceGeneration
                {
                    Report = new DailyAdviceGenerationReport
                    {
                        Kind = DailyAdviceRecoveryTarget.Outline,
                        Succeeded = false,
                        Error = result.Message
                    }
                });
    }

    /// <summary>
    /// Generates one language's column from the outline and returns it together with
    /// generation telemetry. Stub default wraps the plain overload with no telemetry.
    /// </summary>
    public virtual async Task<ResponseWrapper<DailyAdviceGeneration>> GenerateDailyAdviceFromOutlineWithReportAsync(
        string outline, string languageCode, CancellationToken cancellationToken = default)
    {
        ResponseWrapper<string> result = await GenerateDailyAdviceFromOutlineAsync(outline, languageCode, cancellationToken);
        return result.IsSuccess
            ? ResponseWrapper<DailyAdviceGeneration>.Success(new DailyAdviceGeneration { Text = result.Data })
            : ResponseWrapper<DailyAdviceGeneration>.Fail(result.ErrorCode, result.Message,
                new DailyAdviceGeneration
                {
                    Report = new DailyAdviceGenerationReport
                    {
                        Kind = languageCode,
                        Succeeded = false,
                        Error = result.Message
                    }
                });
    }

    /// <summary>
    /// Classifies a user on the 0-5 scam/spam/fake scale using the regular AI model.
    /// The rationale must stay very short and simple. Override in derived classes;
    /// the stub fails so dev flows without a live provider are explicit about it.
    /// </summary>
    public virtual Task<ResponseWrapper<ScamClassification>> ClassifyUserAsync(
        ScamClassificationContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ResponseWrapper<ScamClassification>.Fail(
            ErrorCodes.AIServiceUnavailable,
            "Scam classification is not available in this build (stub implementation)."));
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
