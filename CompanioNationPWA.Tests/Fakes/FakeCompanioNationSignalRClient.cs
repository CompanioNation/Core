using CompanioNation.Shared;
using Microsoft.AspNetCore.Components.Forms;

namespace CompanioNationPWA.Tests.Fakes;

/// <summary>
/// In-memory <see cref="ICompanioNationSignalRClient"/> for bUnit tests.
/// Methods return safe defaults and the most commonly asserted operations can be
/// overridden through handler delegates or settable properties.
/// </summary>
public class FakeCompanioNationSignalRClient : ICompanioNationSignalRClient
{
    public event Action? OnLoginRequested;
    public event Action? OnSubscriptionRequested;
    public event Action? OnHubConnecting;
    public event Action? OnHubConnected;
    public event Action? OnHubDisconnected;
    public event Action? OnStateHasChanged;
    public event Action? OnUpdateAvailable;

    public UserDetails? CurrentUser { get; set; }
    public bool IsPrerendering { get; set; }

    public int InitializeCallCount { get; private set; }
    public int RequestLoginCallCount { get; private set; }
    public int LogoutCallCount { get; private set; }
    public int UpdateUserDetailsCallCount { get; private set; }
    public UserDetails? LastUpdatedUserDetails { get; private set; }

    public Func<Task<bool>>? UpdateUserDetailsAsyncHandler { get; set; }
    public Func<IBrowserFile, Task<(int, Guid)>>? UploadPhotoAsyncHandler { get; set; }
    public Func<int, Task<ResponseWrapper<bool>>>? AcceptTermsAsyncHandler { get; set; }

    public Task RequestLogin()
    {
        RequestLoginCallCount++;
        OnLoginRequested?.Invoke();
        return Task.CompletedTask;
    }

    public void RequestSubscription()
    {
        OnSubscriptionRequested?.Invoke();
    }

    public Task Initialize()
    {
        InitializeCallCount++;
        return Task.CompletedTask;
    }

    public Task<string> GetPWAVersion() => Task.FromResult("test-version");
    public Task<string> GetCurrentVersion() => Task.FromResult("test-version");

    public Task LogError<T>(ResponseWrapper<T> error) => Task.CompletedTask;
    public Task LogError(Exception i_ex) => Task.CompletedTask;
    public Task LogError(Exception i_ex, string? i_additionalInfo) => Task.CompletedTask;
    public Task LogError(string i_message) => Task.CompletedTask;
    public Task LogError(string i_message, Exception? i_ex, string? i_additionalInfo) => Task.CompletedTask;
    public Task LogClientError(ClientErrorReport errorReport) => Task.CompletedTask;
    public Task LogErrorPassive(string i_message) => Task.CompletedTask;

    public Task SetMessageCount(int messageCount) => Task.CompletedTask;
    public Task UpdatePushToken(string pushToken) => Task.CompletedTask;

    public Task Logout()
    {
        LogoutCallCount++;
        CurrentUser = null;
        return Task.CompletedTask;
    }

    public Task RefreshCurrentUserAsync() => Task.CompletedTask;

    public Task<ResponseWrapper<UserDetails>> Login(string i_email, string i_password) =>
        Task.FromResult(ResponseWrapper<UserDetails>.Success(CurrentUser ?? new UserDetails()));

    public Task<ResponseWrapper<UserDetails>> LoginWithGoogle(string code, string code_verifier, string redirect_uri) =>
        Task.FromResult(ResponseWrapper<UserDetails>.Success(CurrentUser ?? new UserDetails()));

    public Task<ResponseWrapper<UserDetails>> LoginWithApple(string code, string redirect_uri, string? firstName, string? lastName) =>
        Task.FromResult(ResponseWrapper<UserDetails>.Success(CurrentUser ?? new UserDetails()));

    public Task<ResponseWrapper<UserDetails>> LoginWithFacebook(string code, string code_verifier, string redirect_uri) =>
        Task.FromResult(ResponseWrapper<UserDetails>.Success(CurrentUser ?? new UserDetails()));

    public Task<ResponseWrapper<UserDetails>> LoginWithTwitter(string code, string code_verifier, string redirect_uri) =>
        Task.FromResult(ResponseWrapper<UserDetails>.Success(CurrentUser ?? new UserDetails()));

    public Task<ResponseWrapper<UserDetails>> LoginWithMicrosoft(string code, string code_verifier, string redirect_uri) =>
        Task.FromResult(ResponseWrapper<UserDetails>.Success(CurrentUser ?? new UserDetails()));

    public Task<ResponseWrapper<OAuthConfig>> GetOAuthConfig() =>
        Task.FromResult(ResponseWrapper<OAuthConfig>.Success(new OAuthConfig()));

    public Task<CheckEmailResult> CheckEmailExists(string email) =>
        Task.FromResult(new CheckEmailResult { emailExists = false, oauthRequired = false });

    public Task<bool> CreateNewUser(string email, string password) => Task.FromResult(true);
    public Task<bool> SendAccountCreationEmail(string email) => Task.FromResult(true);
    public Task<bool> RequestPasswordReset(string i_email) => Task.FromResult(true);
    public Task<bool> ResendVerificationEmail() => Task.FromResult(true);
    public Task<bool> CheckVerificationCode(string i_verificationCode) => Task.FromResult(true);
    public Task<bool> ResetPassword(string i_verificationCode, string i_newPassword) => Task.FromResult(true);
    public Task<string> GetLoginGuid() => Task.FromResult(CurrentUser?.LoginToken?.ToString() ?? string.Empty);
    public Task<bool> IsLoggedIn() => Task.FromResult(CurrentUser is not null);

    public Task<ResponseWrapper<bool>> AcceptTermsAsync(int version)
    {
        if (AcceptTermsAsyncHandler is not null)
        {
            return AcceptTermsAsyncHandler(version);
        }

        return Task.FromResult(ResponseWrapper<bool>.Success(true));
    }

    public Task<bool> UpdateUserDetailsAsync(UserDetails userDetails)
    {
        UpdateUserDetailsCallCount++;
        LastUpdatedUserDetails = userDetails;

        if (UpdateUserDetailsAsyncHandler is not null)
        {
            return UpdateUserDetailsAsyncHandler();
        }

        CurrentUser = userDetails;
        return Task.FromResult(true);
    }

    public Task<(int, Guid)> UploadPhotoAsync(IBrowserFile file)
    {
        if (UploadPhotoAsyncHandler is not null)
        {
            return UploadPhotoAsyncHandler(file);
        }

        return Task.FromResult((0, Guid.NewGuid()));
    }

    public Task<bool> DeleteProfileAsync()
    {
        CurrentUser = null;
        return Task.FromResult(true);
    }

    public Task<Settings> GetSettingsAsync() => Task.FromResult(new Settings());
    public Task<List<UserImage>> GetUserImagesAsync() => Task.FromResult(new List<UserImage>());
    public Task UpdateImageVisibility(int imageId, bool isVisible) => Task.CompletedTask;
    public Task<bool> DeleteUserPhotoAsync(int imageId) => Task.FromResult(true);
    public Task<ResponseWrapper<string>> RequestEmailChangeAsync(string newEmail) =>
        Task.FromResult(ResponseWrapper<string>.Success("verification-code"));
    public Task<ResponseWrapper<bool>> ConfirmEmailChangeAsync(string verificationCode) =>
        Task.FromResult(ResponseWrapper<bool>.Success(true));

    public Task<List<Country>> GetCountries(string continent) => Task.FromResult(new List<Country>());
    public Task<List<City>> GetNearbyCities() => Task.FromResult(new List<City>());
    public Task<List<City>> GetCities(string country, string searchTerm) => Task.FromResult(new List<City>());
    public Task<City> GetCity(int geonameid) => Task.FromResult(new City());
    public Task<List<City>> GetNearestCities(double latitude, double longitude) => Task.FromResult(new List<City>());

    public Task<List<Companion>> GetContestLeaderBoard() => Task.FromResult(new List<Companion>());
    public Task<CompanioNitaAdvice> GetCompanionitaAdviceById(int adviceId) => Task.FromResult(new CompanioNitaAdvice());
    public Task<List<CompanioNitaAdvice>> GetCompanionitaAdvice(int start, int count) => Task.FromResult(new List<CompanioNitaAdvice>());
    public Task<string> AskCompanioNita(int threadId, string i_message) => Task.FromResult(string.Empty);
    public Task<string> StreamAskCompanioNitaAsync(int threadId, string i_message, Action<string> onChunkReceived, Action<string>? onReasoningReceived = null) => Task.FromResult(string.Empty);
    public Task<string> StreamAskCompanioNitaAboutConversationAsync(int userId, Action<string> onChunkReceived, Action<string>? onReasoningReceived = null) => Task.FromResult(string.Empty);
    public Task<int> StartAdviceThreadAsync() => Task.FromResult(0);
    public Task<List<AdviceThread>> GetAdviceThreadsAsync() => Task.FromResult(new List<AdviceThread>());
    public Task<List<AdviceExchange>> GetAdviceExchangesAsync(int threadId) => Task.FromResult(new List<AdviceExchange>());
    public Task SaveActiveThreadIdAsync(int threadId) => Task.CompletedTask;
    public Task<int> GetActiveThreadIdAsync() => Task.FromResult(0);
    public Task<List<Advice>> GetAdvice() => Task.FromResult(new List<Advice>());

    public Task<List<UserConversation>> GetUserConversationsAsync() => Task.FromResult(new List<UserConversation>());
    public Task<UserConversation> StartUserConversationAsync(int userId) => Task.FromResult(new UserConversation());
    public Task<List<UserMessage>> GetMessagesWithUserAsync(int userId) => Task.FromResult(new List<UserMessage>());
    public Task<List<UserMessage>> GetIgnoredMessagesAsync() => Task.FromResult(new List<UserMessage>());
    public Task<int> SendMessageAsync(int userId, string messageText) => Task.FromResult(0);
    public Task<bool> AddIgnore(int userId) => Task.FromResult(true);
    public Task<bool> RemoveIgnore(int userId) => Task.FromResult(true);
    public Task<ReportResult?> ReportUserAsync(ReportRequest request) => Task.FromResult<ReportResult?>(new ReportResult(1));

    public Task<List<Companion>> FindCompanionsAsync(
        bool cisMale,
        bool cisFemale,
        bool other,
        bool transMale,
        bool transFemale,
        List<int> cities,
        int? ageFrom,
        int? ageTo,
        bool showIgnoredUsers) => Task.FromResult(new List<Companion>());

    public Task<bool> GuaranteeConfirm(string verificationCode) => Task.FromResult(true);
    public Task<int> GuaranteeUser(string email, byte[] imageData) => Task.FromResult(0);
    public Task<int> GuaranteeUser(string email) => Task.FromResult(0);
    public Task<List<GuaranteedUser>> GetGuaranteedUsersAsync() => Task.FromResult(new List<GuaranteedUser>());
    public Task<bool> RemoveGuaranteeAsync(int imageId) => Task.FromResult(true);
    public Task<bool> UpdateImageReview(int imageId, int rating, string review) => Task.FromResult(true);
    public Task<bool> UpdateReviewVisibility(int imageId, bool isPublic) => Task.FromResult(true);

    public Task<string> GetLinkPayloadAsync() => Task.FromResult(string.Empty);
    public Task<(LinkedUser? Data, int ErrorCode)> RedeemQrLinkAsync(string code) => Task.FromResult((default(LinkedUser?), 0));
    public Task<int> LinkEmailAsync(string email) => Task.FromResult(0);
    public Task<ResponseWrapper<UserDetails>> ConfirmEmailLinkAsync(string verificationCode) =>
        Task.FromResult(ResponseWrapper<UserDetails>.Success(CurrentUser ?? new UserDetails()));
    public Task<ResponseWrapper<string>> RejectEmailLinkAsync(string verificationCode) =>
        Task.FromResult(ResponseWrapper<string>.Success(string.Empty));
    public Task<List<LinkedUser>> GetLinkedUsersAsync() => Task.FromResult(new List<LinkedUser>());
    public Task<int> UploadLinkPhotoAsync(int connectionId, byte[] imageData) => Task.FromResult(0);
    public Task<bool> SetLinkPhotoVisibilityAsync(int imageId, bool visible) => Task.FromResult(true);
    public Task<bool> ConfirmLinkPhotoAsync(int imageId) => Task.FromResult(true);
    public Task<bool> RejectLinkPhotoAsync(int imageId) => Task.FromResult(true);
    public Task<bool> SetConnectionReview(int connectionId, int rating, string review) => Task.FromResult(true);
    public Task<bool> SetConnectionReviewVisibility(int connectionId, bool isVisible) => Task.FromResult(true);

    public Task<List<KarmaDesync>> RecalculateKarmaAsync() => Task.FromResult(new List<KarmaDesync>());
    public Task<GuarantorMigrationResult?> MigrateGuarantorDataAsync() => Task.FromResult<GuarantorMigrationResult?>(null);
    public Task<string> TriggerMaintenanceManually() => Task.FromResult(string.Empty);
    public Task<string> RunTestSuite() => Task.FromResult(string.Empty);
    public Task SendFeedback(string feedbackText) => Task.CompletedTask;

    public Task<ResponseWrapper<SiteStats>> AdminGetSiteStatsAsync() =>
        Task.FromResult(ResponseWrapper<SiteStats>.Success(new SiteStats()));
    public Task<ResponseWrapper<List<UserDetails>>> AdminGetFlaggedProfilesAsync(int offset, int count, string? searchTerm = null) =>
        Task.FromResult(ResponseWrapper<List<UserDetails>>.Success(new List<UserDetails>()));
    public Task<ResponseWrapper<UserDetails>> AdminGetProfileForAuditAsync(int userId) =>
        Task.FromResult(ResponseWrapper<UserDetails>.Success(new UserDetails()));
    public Task<ResponseWrapper<bool>> AdminUpdateProfileAsync(UserDetails userDetails) =>
        Task.FromResult(ResponseWrapper<bool>.Success(true));
    public Task<ResponseWrapper<bool>> AdminUpdateUserAttributesAsync(AdminUserAttributes attributes) =>
        Task.FromResult(ResponseWrapper<bool>.Success(true));
    public Task<ResponseWrapper<bool>> AdminDeletePhotoAsync(int userId, int imageId) =>
        Task.FromResult(ResponseWrapper<bool>.Success(true));
    public Task<ResponseWrapper<List<EventBadge>>> GetUserBadgesAsync(int targetUserId) =>
        Task.FromResult(ResponseWrapper<List<EventBadge>>.Success(new List<EventBadge>()));
    public Task<ResponseWrapper<List<EventBadge>>> AdminListEventBadgesAsync() =>
        Task.FromResult(ResponseWrapper<List<EventBadge>>.Success(new List<EventBadge>()));
    public Task<ResponseWrapper<bool>> AdminAwardEventBadgeAsync(int targetUserId, int badgeId) =>
        Task.FromResult(ResponseWrapper<bool>.Success(true));
    public Task<ResponseWrapper<bool>> AdminRevokeEventBadgeAsync(int targetUserId, int badgeId) =>
        Task.FromResult(ResponseWrapper<bool>.Success(true));
    public Task<ResponseWrapper<BroadcastResult>> AdminSendBroadcastNotificationAsync(string title, string body, string? url, string? targetEmail = null) =>
        Task.FromResult(ResponseWrapper<BroadcastResult>.Success(new BroadcastResult(0, 0, 0)));
    public Task<ResponseWrapper<List<OrphanedImage>>> AdminFindOrphanedImagesAsync() =>
        Task.FromResult(ResponseWrapper<List<OrphanedImage>>.Success(new List<OrphanedImage>()));
    public Task<ResponseWrapper<int>> AdminDeleteOrphanedImagesAsync(List<Guid> imageGuids) =>
        Task.FromResult(ResponseWrapper<int>.Success(0));
    public Task<ResponseWrapper<bool>> AdminDismissProfileAsync(int userId) =>
        Task.FromResult(ResponseWrapper<bool>.Success(true));
    public Task<ResponseWrapper<bool>> AdminDeleteProfileAsync(int userId) =>
        Task.FromResult(ResponseWrapper<bool>.Success(true));
    public Task<ResponseWrapper<string>> AdminCheckPhotoAsync(Guid imageGuid) =>
        Task.FromResult(ResponseWrapper<string>.Success(string.Empty));
    public Task AdminCheckAllPhotosAsync(Action<string> onProgress, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<bool> SetMuteStatusAsync(int targetUserId, bool isMuted) => Task.FromResult(true);
    public Task<bool> ResolveReportAsync(int reportId, int status) => Task.FromResult(true);
    public Task<List<PendingReport>> GetPendingReportsAsync() => Task.FromResult(new List<PendingReport>());
}
