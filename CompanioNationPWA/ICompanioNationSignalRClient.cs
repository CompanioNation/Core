using CompanioNation.Shared;
using Microsoft.AspNetCore.Components.Forms;

namespace CompanioNationPWA;

/// <summary>
/// Abstraction over the long-lived SignalR client so Blazor components can be
/// exercised in tests without a live hub connection.
/// </summary>
public interface ICompanioNationSignalRClient
{
    event Action OnLoginRequested;
    event Action OnSubscriptionRequested;
    event Action OnHubConnecting;
    event Action OnHubConnected;
    event Action OnHubDisconnected;
    event Action OnStateHasChanged;
    event Action OnUpdateAvailable;

    bool IsPrerendering { get; }
    UserDetails? CurrentUser { get; }

    Task RequestLogin();
    void RequestSubscription();
    Task Initialize();
    Task<string> GetPWAVersion();
    Task<string> GetCurrentVersion();

    Task LogError<T>(ResponseWrapper<T> error);
    Task LogError(Exception i_ex);
    Task LogError(Exception i_ex, string? i_additionalInfo);
    Task LogError(string i_message);
    Task LogError(string i_message, Exception? i_ex, string? i_additionalInfo);
    Task LogClientError(ClientErrorReport errorReport);
    Task LogErrorPassive(string i_message);

    Task SetMessageCount(int messageCount);
    Task UpdatePushToken(string pushToken);
    Task Logout();
    Task RefreshCurrentUserAsync();

    Task<ResponseWrapper<UserDetails>> Login(string i_email, string i_password);
    Task<ResponseWrapper<UserDetails>> LoginWithGoogle(string code, string code_verifier, string redirect_uri);
    Task<ResponseWrapper<UserDetails>> LoginWithApple(string code, string redirect_uri, string? firstName, string? lastName);
    Task<ResponseWrapper<UserDetails>> LoginWithFacebook(string code, string code_verifier, string redirect_uri);
    Task<ResponseWrapper<UserDetails>> LoginWithTwitter(string code, string code_verifier, string redirect_uri);
    Task<ResponseWrapper<UserDetails>> LoginWithMicrosoft(string code, string code_verifier, string redirect_uri);
    Task<ResponseWrapper<OAuthConfig>> GetOAuthConfig();
    Task<CheckEmailResult> CheckEmailExists(string email);
    Task<bool> CreateNewUser(string email, string password);
    Task<bool> SendAccountCreationEmail(string email);
    Task<bool> RequestNewVerificationCode(string i_email);
    Task<bool> CheckVerificationCode(string i_verificationCode);
    Task<bool> ResetPassword(string i_verificationCode, string i_newPassword);
    Task<string> GetLoginGuid();
    Task<bool> IsLoggedIn();

    Task<ResponseWrapper<bool>> AcceptTermsAsync(int version);
    Task<bool> UpdateUserDetailsAsync(UserDetails userDetails);
    Task<(int, Guid)> UploadPhotoAsync(IBrowserFile file);
    Task<bool> DeleteProfileAsync();
    Task<Settings> GetSettingsAsync();
    Task<List<UserImage>> GetUserImagesAsync();
    Task UpdateImageVisibility(int imageId, bool isVisible);
    Task<bool> DeleteUserPhotoAsync(int imageId);
    Task<ResponseWrapper<string>> RequestEmailChangeAsync(string newEmail);
    Task<ResponseWrapper<bool>> ConfirmEmailChangeAsync(string verificationCode);

    Task<List<Country>> GetCountries(string continent);
    Task<List<City>> GetNearbyCities();
    Task<List<City>> GetCities(string country, string searchTerm);
    Task<City> GetCity(int geonameid);
    Task<List<City>> GetNearestCities(double latitude, double longitude);

    Task<List<Companion>> GetContestLeaderBoard();
    Task<CompanioNitaAdvice> GetCompanionitaAdviceById(int adviceId);
    Task<List<CompanioNitaAdvice>> GetCompanionitaAdvice(int start, int count);
    Task<string> AskCompanioNita(int threadId, string i_message);
    Task<string> StreamAskCompanioNitaAsync(int threadId, string i_message, Action<string> onChunkReceived);
    Task<string> StreamAskCompanioNitaAboutConversationAsync(int userId, Action<string> onChunkReceived);
    Task<int> StartAdviceThreadAsync();
    Task<List<AdviceThread>> GetAdviceThreadsAsync();
    Task<List<AdviceExchange>> GetAdviceExchangesAsync(int threadId);
    Task SaveActiveThreadIdAsync(int threadId);
    Task<int> GetActiveThreadIdAsync();
    Task<List<Advice>> GetAdvice();

    Task<List<UserConversation>> GetUserConversationsAsync();
    Task<UserConversation> StartUserConversationAsync(int userId);
    Task<List<UserMessage>> GetMessagesWithUserAsync(int userId);
    Task<List<UserMessage>> GetIgnoredMessagesAsync();
    Task<int> SendMessageAsync(int userId, string messageText);
    Task<bool> AddIgnore(int userId);
    Task<bool> RemoveIgnore(int userId);
    Task<ReportResult?> ReportUserAsync(ReportRequest request);

    Task<List<Companion>> FindCompanionsAsync(
        bool cisMale,
        bool cisFemale,
        bool other,
        bool transMale,
        bool transFemale,
        List<int> cities,
        int? ageFrom,
        int? ageTo,
        bool showIgnoredUsers);

    Task<bool> GuaranteeConfirm(string verificationCode);
    Task<int> GuaranteeUser(string email, byte[] imageData);
    Task<int> GuaranteeUser(string email);
    Task<List<GuaranteedUser>> GetGuaranteedUsersAsync();
    Task<bool> RemoveGuaranteeAsync(int imageId);
    Task<bool> UpdateImageReview(int imageId, int rating, string review);
    Task<bool> UpdateReviewVisibility(int imageId, bool isPublic);

    Task<string> GetLinkPayloadAsync();
    Task<(LinkedUser? Data, int ErrorCode)> RedeemQrLinkAsync(string code);
    Task<int> LinkEmailAsync(string email);
    Task<ResponseWrapper<UserDetails>> ConfirmEmailLinkAsync(string verificationCode);
    Task<ResponseWrapper<string>> RejectEmailLinkAsync(string verificationCode);
    Task<List<LinkedUser>> GetLinkedUsersAsync();
    Task<int> UploadLinkPhotoAsync(int connectionId, byte[] imageData);
    Task<bool> SetLinkPhotoVisibilityAsync(int imageId, bool visible);
    Task<bool> ConfirmLinkPhotoAsync(int imageId);
    Task<bool> RejectLinkPhotoAsync(int imageId);
    Task<bool> SetConnectionReview(int connectionId, int rating, string review);
    Task<bool> SetConnectionReviewVisibility(int connectionId, bool isVisible);

    Task<List<KarmaDesync>> RecalculateKarmaAsync();
    Task<GuarantorMigrationResult?> MigrateGuarantorDataAsync();
    Task<string> TriggerMaintenanceManually();
    Task<string> RunTestSuite();
    Task SendFeedback(string feedbackText);

    Task<ResponseWrapper<SiteStats>> AdminGetSiteStatsAsync();
    Task<ResponseWrapper<List<UserDetails>>> AdminGetFlaggedProfilesAsync(int offset, int count, string? searchTerm = null);
    Task<ResponseWrapper<UserDetails>> AdminGetProfileForAuditAsync(int userId);
    Task<ResponseWrapper<bool>> AdminUpdateProfileAsync(UserDetails userDetails);
    Task<ResponseWrapper<bool>> AdminDeletePhotoAsync(int userId, int imageId);
    Task<ResponseWrapper<List<OrphanedImage>>> AdminFindOrphanedImagesAsync();
    Task<ResponseWrapper<int>> AdminDeleteOrphanedImagesAsync(List<Guid> imageGuids);
    Task<ResponseWrapper<bool>> AdminDismissProfileAsync(int userId);
    Task<ResponseWrapper<bool>> AdminDeleteProfileAsync(int userId);
    Task<ResponseWrapper<string>> AdminCheckPhotoAsync(Guid imageGuid);
    Task AdminCheckAllPhotosAsync(Action<string> onProgress, CancellationToken cancellationToken);
    Task<bool> SetMuteStatusAsync(int targetUserId, bool isMuted);
    Task<bool> ResolveReportAsync(int reportId, int status);
    Task<List<PendingReport>> GetPendingReportsAsync();
}
