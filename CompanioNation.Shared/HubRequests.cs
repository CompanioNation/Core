namespace CompanioNation.Shared
{
    /// <summary>
    /// Contract metadata shared by the hub boundary. Every hub method now takes exactly
    /// ONE request object whose arity never changes, so cached old clients can never fail
    /// at SignalR argument binding again — a breaking change becomes a new request type on
    /// a new method name, while ADDITIVE fields on an existing request remain backward
    /// compatible (old clients simply omit them and they deserialize to null/default).
    /// </summary>
    public static class HubContract
    {
        /// <summary>
        /// The oldest client version that may call the single-DTO hub methods. This is the
        /// version where the DTO migration shipped; every method compares the request's
        /// ClientVersion against this floor and returns ClientUpgradeRequired when older.
        /// Bump a specific method's floor later only when it makes an incompatible change.
        /// </summary>
        public const string MinimumClientVersion = "26.9.3.0";
    }

    /// <summary>Base type carried by every hub request.</summary>
    public record HubRequest
    {
        /// <summary>The client's assembly version at the time of the call (e.g. "26.9.2.4").</summary>
        public string? ClientVersion { get; init; }
    }

    // ── Authentication / session ──
    public sealed record LoginRequest : HubRequest { public string? Email { get; init; } public string? Password { get; init; } }
    public sealed record LoginWithGoogleRequest : HubRequest { public string? Code { get; init; } public string? CodeVerifier { get; init; } public string? RedirectUri { get; init; } }
    public sealed record LoginWithAppleRequest : HubRequest { public string? Code { get; init; } public string? RedirectUri { get; init; } public string? FirstName { get; init; } public string? LastName { get; init; } }
    public sealed record LoginWithFacebookRequest : HubRequest { public string? Code { get; init; } public string? CodeVerifier { get; init; } public string? RedirectUri { get; init; } }
    public sealed record LoginWithTwitterRequest : HubRequest { public string? Code { get; init; } public string? CodeVerifier { get; init; } public string? RedirectUri { get; init; } }
    public sealed record LoginWithMicrosoftRequest : HubRequest { public string? Code { get; init; } public string? CodeVerifier { get; init; } public string? RedirectUri { get; init; } }
    public sealed record AcceptTermsRequest : HubRequest { public string? LoginToken { get; init; } public int Version { get; init; } }
    public sealed record GetOAuthConfigRequest : HubRequest { }
    public sealed record ConnectRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record GetCurrentVersionRequest : HubRequest { }
    public sealed record RequestPasswordResetRequest : HubRequest { public string? Email { get; init; } }
    public sealed record CheckVerificationCodeRequest : HubRequest { public string? VerificationCode { get; init; } }
    public sealed record ResetPasswordRequest : HubRequest { public string? VerificationCode { get; init; } public string? NewPassword { get; init; } }
    public sealed record ResendVerificationEmailRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record ConfirmEmailChangeRequest : HubRequest { public string? LoginToken { get; init; } public string? VerificationCode { get; init; } }
    public sealed record RequestEmailChangeRequest : HubRequest { public string? LoginToken { get; init; } public string? NewEmail { get; init; } }

    // ── Logging ──
    public sealed record LogErrorRequest : HubRequest { public DateTime Timestamp { get; init; } public string? Message { get; init; } public string? Version { get; init; } }
    public sealed record LogClientErrorRequest : HubRequest { public ClientErrorReport? Report { get; init; } }

    // ── CompanioNita / advice ──
    public sealed record GetContestLeaderBoardRequest : HubRequest { }
    public sealed record GetCompanioNitaAdviceByIdRequest : HubRequest { public int AdviceId { get; init; } public string? LanguageCode { get; init; } }
    public sealed record GetCompanioNitaAdviceRequest : HubRequest { public int Start { get; init; } public int Count { get; init; } public string? LanguageCode { get; init; } }
    public sealed record AskCompanioNitaRequest : HubRequest { public string? LoginToken { get; init; } public int ThreadId { get; init; } public string? Message { get; init; } }
    public sealed record StreamAskCompanioNitaRequest : HubRequest { public string? LoginToken { get; init; } public int ThreadId { get; init; } public string? Message { get; init; } }
    public sealed record GetAdviceRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record StartAdviceThreadRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record GetAdviceThreadsRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record GetAdviceExchangesRequest : HubRequest { public string? LoginToken { get; init; } public int ThreadId { get; init; } }
    public sealed record StreamAskCompanioNitaAboutConversationRequest : HubRequest { public string? LoginToken { get; init; } public int UserId { get; init; } }

    // ── Social / reporting ──
    public sealed record AddIgnoreRequest : HubRequest { public string? LoginToken { get; init; } public int UserId { get; init; } }
    public sealed record RemoveIgnoreRequest : HubRequest { public string? LoginToken { get; init; } public int UserId { get; init; } }
    public sealed record ReportUserRequest : HubRequest { public string? LoginToken { get; init; } public ReportRequest? Report { get; init; } }
    public sealed record GetPendingReportsRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record ResolveReportRequest : HubRequest { public string? LoginToken { get; init; } public int ReportId { get; init; } public int Status { get; init; } }
    public sealed record SetMuteStatusRequest : HubRequest { public string? LoginToken { get; init; } public int TargetUserId { get; init; } public bool IsMuted { get; init; } }

    // ── Guarantee / verification ──
    public sealed record GuaranteeConfirmRequest : HubRequest { public string? VerificationCode { get; init; } }
    public sealed record GuaranteeEmailRequest : HubRequest { public string? LoginToken { get; init; } public string? Email { get; init; } }
    public sealed record GuaranteeUserRequest : HubRequest { public string? LoginToken { get; init; } public string? Email { get; init; } public byte[]? ImageData { get; init; } }
    public sealed record UploadPhotoRequest : HubRequest { public string? LoginToken { get; init; } public byte[]? ImageData { get; init; } }
    public sealed record GetGuaranteedUsersRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record StartUserConversationRequest : HubRequest { public string? LoginToken { get; init; } public int UserId { get; init; } }
    public sealed record RemoveGuaranteeRequest : HubRequest { public string? LoginToken { get; init; } public int ImageId { get; init; } }

    // ── Profile / settings / messaging ──
    public sealed record GetSettingsRequest : HubRequest { public string? LanguageCode { get; init; } }
    public sealed record GetUserImagesRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record GetUserConversationsRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record GetMessagesWithUserRequest : HubRequest { public string? LoginToken { get; init; } public int UserId { get; init; } }
    public sealed record SendMessageRequest : HubRequest { public string? LoginToken { get; init; } public int UserId { get; init; } public string? MessageText { get; init; } }
    public sealed record UpdatePushTokenRequest : HubRequest { public string? LoginToken { get; init; } public string? PushToken { get; init; } }
    public sealed record GetIgnoredMessagesRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record FindCompanionsRequest : HubRequest { public string? LoginToken { get; init; } public bool CisMale { get; init; } public bool CisFemale { get; init; } public bool Other { get; init; } public bool TransMale { get; init; } public bool TransFemale { get; init; } public List<int>? Cities { get; init; } public int AgeMin { get; init; } public int AgeMax { get; init; } public bool ShowIgnoredUsers { get; init; } }
    public sealed record UpdateUserDetailsRequest : HubRequest { public string? LoginToken { get; init; } public UserDetails? UserDetails { get; init; } }
    public sealed record DeleteProfileRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record UpdateImageVisibilityRequest : HubRequest { public string? LoginToken { get; init; } public int ImageId { get; init; } public bool IsVisible { get; init; } }
    public sealed record UpdateReviewVisibilityRequest : HubRequest { public string? LoginToken { get; init; } public int ImageId { get; init; } public bool IsPublic { get; init; } }
    public sealed record UpdateImageReviewRequest : HubRequest { public string? LoginToken { get; init; } public int ImageId { get; init; } public int Rating { get; init; } public string? Review { get; init; } }
    public sealed record SetConnectionReviewRequest : HubRequest { public string? LoginToken { get; init; } public int ConnectionId { get; init; } public int Rating { get; init; } public string? Review { get; init; } }
    public sealed record SetConnectionReviewVisibilityRequest : HubRequest { public string? LoginToken { get; init; } public int ConnectionId { get; init; } public bool IsVisible { get; init; } }

    // ── Maintenance / geo ──
    public sealed record TriggerMaintenanceManuallyRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record GetCountriesRequest : HubRequest { public string? Continent { get; init; } }
    public sealed record GetNearbyCitiesRequest : HubRequest { public string? LoginToken { get; init; } }

    // ── Feedback / diagnostics ──
    public sealed record ReceiveFeedbackRequest : HubRequest { public string? LoginToken { get; init; } public string? FeedbackText { get; init; } public string? FeedbackDebugInfo { get; init; } }
    public sealed record RunTestSuiteRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record GeneratePngImageRequest : HubRequest { public int Width { get; init; } public int Height { get; init; } public int BackgroundColorArgb { get; init; } }

    // ── Admin ──
    public sealed record AdminGetFlaggedProfilesRequest : HubRequest { public string? LoginToken { get; init; } public int Offset { get; init; } public int Count { get; init; } public string? SearchTerm { get; init; } }
    public sealed record AdminGetProfileForAuditRequest : HubRequest { public string? LoginToken { get; init; } public int UserId { get; init; } }
    public sealed record AdminUpdateProfileRequest : HubRequest { public string? LoginToken { get; init; } public UserDetails? UserDetails { get; init; } }
    public sealed record AdminUpdateUserAttributesRequest : HubRequest { public string? LoginToken { get; init; } public AdminUserAttributes? Attributes { get; init; } }
    public sealed record AdminDeletePhotoRequest : HubRequest { public string? LoginToken { get; init; } public int UserId { get; init; } public int ImageId { get; init; } }
    public sealed record GetUserBadgesRequest : HubRequest { public string? LoginToken { get; init; } public int TargetUserId { get; init; } }
    public sealed record AdminListEventBadgesRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record AdminAwardEventBadgeRequest : HubRequest { public string? LoginToken { get; init; } public int TargetUserId { get; init; } public int BadgeId { get; init; } }
    public sealed record AdminRevokeEventBadgeRequest : HubRequest { public string? LoginToken { get; init; } public int TargetUserId { get; init; } public int BadgeId { get; init; } }
    public sealed record AdminSendBroadcastNotificationRequest : HubRequest { public string? LoginToken { get; init; } public string? Title { get; init; } public string? Body { get; init; } public string? Url { get; init; } public string? TargetEmail { get; init; } }

    // ── Maintenance / geo ──
    public sealed record GetCitiesRequest : HubRequest { public string? Country { get; init; } public string? SearchTerm { get; init; } }
    public sealed record GetCityRequest : HubRequest { public string? LoginToken { get; init; } public int Geonameid { get; init; } }
    public sealed record GetNearestCitiesRequest : HubRequest { public string? LoginToken { get; init; } public double Latitude { get; init; } public double Longitude { get; init; } }
    public sealed record CheckEmailExistsRequest : HubRequest { public string? Email { get; init; } }
    public sealed record CreateNewUserRequest : HubRequest { public string? Email { get; init; } public string? Password { get; init; } }

    // ── Admin (additional) ──
    public sealed record AdminFindOrphanedImagesRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record AdminDeleteOrphanedImagesRequest : HubRequest { public string? LoginToken { get; init; } public List<Guid>? ImageGuids { get; init; } }
    public sealed record AdminDismissProfileRequest : HubRequest { public string? LoginToken { get; init; } public int UserId { get; init; } }
    public sealed record AdminDeleteProfileRequest : HubRequest { public string? LoginToken { get; init; } public int UserId { get; init; } }
    public sealed record AdminGetSiteStatsRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record AdminCheckPhotoRequest : HubRequest { public string? LoginToken { get; init; } public Guid ImageGuid { get; init; } }
    public sealed record AdminCheckAllPhotosRequest : HubRequest { public string? LoginToken { get; init; } }

    // ── LINK ──
    public sealed record GetLinkPayloadRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record RedeemQrLinkRequest : HubRequest { public string? LoginToken { get; init; } public string? Code { get; init; } }
    public sealed record LinkEmailRequest : HubRequest { public string? LoginToken { get; init; } public string? Email { get; init; } }
    public sealed record ConfirmEmailLinkRequest : HubRequest { public string? VerificationCode { get; init; } }
    public sealed record RejectEmailLinkRequest : HubRequest { public string? VerificationCode { get; init; } }
    public sealed record GetLinkedUsersRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record UploadLinkPhotoRequest : HubRequest { public string? LoginToken { get; init; } public int ConnectionId { get; init; } public byte[]? ImageData { get; init; } }
    public sealed record DeleteUserPhotoRequest : HubRequest { public string? LoginToken { get; init; } public int ImageId { get; init; } }
    public sealed record SetLinkPhotoVisibilityRequest : HubRequest { public string? LoginToken { get; init; } public int ImageId { get; init; } public bool Visible { get; init; } }
    public sealed record ConfirmLinkPhotoRequest : HubRequest { public string? LoginToken { get; init; } public int ImageId { get; init; } }
    public sealed record RejectLinkPhotoRequest : HubRequest { public string? LoginToken { get; init; } public int ImageId { get; init; } }
    public sealed record RecalculateKarmaRequest : HubRequest { public string? LoginToken { get; init; } }
    public sealed record MigrateGuarantorDataRequest : HubRequest { public string? LoginToken { get; init; } }
}
