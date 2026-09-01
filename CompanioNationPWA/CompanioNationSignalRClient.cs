using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CompanioNation.Shared;
using CompanioNationPWA.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;


namespace CompanioNationPWA
{

    /// <summary>
    /// Long-lived SignalR client that fronts the CompanioNation API hub for the Blazor
    /// WebAssembly PWA. Registered as a <b>singleton</b> in
    /// <see cref="Program"/>, so a single <see cref="HubConnection"/> is shared for the
    /// whole app session.
    ///
    /// <para><b>Connection lifecycle</b> (all in one place — do not reimplement per call):</para>
    /// <list type="bullet">
    ///   <item><see cref="Initialize"/> lazily builds + starts the connection and is safe to
    ///   call before every hub method; it fast-paths when already connected and is guarded
    ///   by <c>_semaphore</c>.</item>
    ///   <item><see cref="BuildHubConnection"/> configures automatic reconnect
    ///   (<see cref="InfiniteRetryPolicy"/>) plus mobile-friendly ServerTimeout /
    ///   KeepAliveInterval / HandshakeTimeout, and wires the Reconnecting / Reconnected /
    ///   Closed handlers.</item>
    ///   <item>The initial connect loop is <b>time-boxed (30s)</b> so a down server can never
    ///   leave the semaphore — and therefore every hub call — blocked forever.</item>
    ///   <item>The <c>Closed</c> handler triggers <see cref="SafeReinitializeAsync"/> so the
    ///   app self-heals in the background without surfacing unobserved task exceptions.</item>
    /// </list>
    ///
    /// <para><b>Calling hub methods — the standard pattern.</b> Prefer
    /// <see cref="InvokeHubAsync{T}"/> (or <see cref="InvokeHubVoidAsync"/> for no-result
    /// calls) for any method that just does Initialize → invoke → (optional
    /// <c>InvalidCredentials</c> → <see cref="RequestLogin"/>) → return. These helpers
    /// centralize connect/retry, treat <see cref="TimeoutException"/> and a dropped
    /// connection as transient, trigger the login prompt on
    /// <see cref="ErrorCodes.InvalidCredentials"/>, and log unexpected errors via
    /// <see cref="LogError(System.Exception, string)"/>. Only hand-roll try/Initialize/catch
    /// when a method needs bespoke handling (e.g. subscription errors via
    /// <see cref="RequestSubscription"/>, streaming, or custom return shaping).</para>
    ///
    /// <para><b>Error logging</b> flows through <see cref="LogError(System.Exception, string)"/>,
    /// which degrades to <c>LogErrorPassive</c> (local storage) when the hub is unavailable
    /// so nothing is lost while offline.</para>
    /// </summary>
    public class CompanioNationSignalRClient : ICompanioNationSignalRClient
    {
        // Define an event that MainLayout can subscribe to
        public event Action OnLoginRequested;
        public event Action OnSubscriptionRequested;
        public event Action OnHubConnecting;
        public event Action OnHubConnected;
        public event Action OnHubDisconnected;
        public event Action OnStateHasChanged;
        public event Action OnUpdateAvailable;
        public async Task RequestLogin()
        {
            // During SSR prerendering, skip browser-only JS calls silently.
            if (_isPrerendering)
            {
                _loginGuid = null;
                OnLoginRequested?.Invoke();
                return;
            }

            // Invalidate the saved login token so hub calls stop re-triggering the
            // login popup with the same bad token. Clear both the in-memory field
            // and the persisted localStorage value.
            _loginGuid = null;
            try { await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "loginGuid"); }
            catch { /* best-effort — localStorage may not be available */ }

            // Cancel the push subscription since the session is no longer valid
            await _jsRuntime.InvokeVoidAsync("window.unregisterPush");

            // Trigger the Login event
            OnLoginRequested?.Invoke();
        }

        public void RequestSubscription()
        {
            // Trigger the Subscription event
            OnSubscriptionRequested?.Invoke();
        }

        private bool IsSubscriptionError(int errorCode)
        {
            return errorCode >= ErrorCodes.SubscriptionRequired && 
                   errorCode <= ErrorCodes.UsageLimitExceeded;
        }

        private class LogEntry
        {
            public DateTime timestamp { get; set; }
            public string message { get; set; }
            public string version { get; set; }
        }


        private readonly IJSRuntime _jsRuntime;
        private readonly NavigationManager _navigationManager;
        private readonly IConfiguration _configuration;
        private HubConnection? _hubConnection;
        private readonly bool _isPrerendering; // True during SSR, when IJSRuntime is a stub

        /// <summary>
        /// True during SSR prerendering when the hub connection and JS interop
        /// are unavailable. Pages can check this to branch their data-loading
        /// strategy (e.g. use HttpClient instead of SignalR).
        /// </summary>
        public bool IsPrerendering => _isPrerendering;

        // The _loginGuid stores the login state token so that we don't have to keep passing in the username and password
        private string? _loginGuid = null;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1); // Keep this static as it's managing shared access

        private string? _currentVersion = null;

        private bool _versionMismatch = false;

        private UserDetails? _currentUser = null;
        public UserDetails? CurrentUser => _currentUser;

        private string GetHubUrl()
        {
            var configuredUrl = _configuration["SignalR:HubUrl"];
            if (string.IsNullOrWhiteSpace(configuredUrl)) configuredUrl = "";
            string absoluteUri = _navigationManager.ToAbsoluteUri(configuredUrl).ToString();
            return absoluteUri;
        }


        public CompanioNationSignalRClient(IJSRuntime i_JS, NavigationManager navigationManager, IConfiguration configuration)
        {
            _jsRuntime = i_JS;
            _navigationManager = navigationManager;
            _configuration = configuration;
            _hubConnection = null;

            // During SSR prerendering, IJSRuntime is a stub that throws. Detect this so
            // we can skip all browser-only operations (hub connection, localStorage, etc.).
            // Check the runtime type name since UnsupportedJavaScriptRuntime isn't public API.
            _isPrerendering = _jsRuntime.GetType().Name == "UnsupportedJavaScriptRuntime";
        }

        public async Task<string> GetPWAVersion()
        {
            return Util.GetCurrentVersion();
        }
        public async Task<string> GetCurrentVersion()
        {
            return _currentVersion;
        }

        // The purpose of this method is to initialize the Hub Connection so that it is
        //  in a Connected state and able to call methods
        public async Task Initialize()
        {
            // During SSR prerendering, skip all hub/browser setup silently.
            if (_isPrerendering) return;

            // Fast path — no need to take the lock when we're already connected.
            if (_hubConnection is { State: HubConnectionState.Connected })
            {
                return;
            }

            await _semaphore.WaitAsync();
            try
            {
                if (_hubConnection is { State: HubConnectionState.Connected })
                {
                    return; // Connected while we were waiting for the lock.
                }

                // The built-in automatic reconnect may already be re-establishing the
                // connection. Give it a chance to finish before we intervene.
                if (_hubConnection is { State: HubConnectionState.Connecting or HubConnectionState.Reconnecting })
                {
                    await WaitForConnectedAsync(TimeSpan.FromSeconds(10));
                    if (_hubConnection.State == HubConnectionState.Connected)
                    {
                        return;
                    }

                    // The built-in reconnect hasn't completed in time. Stop it so
                    // the StartAsync loop below gets a fresh Disconnected connection
                    // instead of throwing on a Reconnecting one.
                    try { await _hubConnection.StopAsync(); }
                    catch (Exception ex) { Console.WriteLine($"StopAsync during reconnect cleanup: {ex.Message}"); }
                }

                // Build the connection once and reuse it; automatic reconnect keeps it
                // alive across transient network drops.
                if (_hubConnection == null)
                {
                    BuildHubConnection();
                }

                OnHubConnecting?.Invoke();

                // (Re)start with bounded, backing-off retries. We deliberately cap the
                // total time so a temporarily unreachable server can never leave this
                // lock — and therefore every hub call — blocked indefinitely.
                DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                int attempt = 0;
                while (true)
                {
                    attempt++;
                    try
                    {
                        await _hubConnection.StartAsync();
                        Console.WriteLine("CONNECTED to the SignalR Hub!");
                        await Connect();
                        OnHubConnected?.Invoke();
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (DateTime.UtcNow >= deadline)
                        {
                            Console.WriteLine($"SignalR initial connect gave up after {attempt} attempt(s): {ex.Message}");
                            OnHubDisconnected?.Invoke();
                            return; // Release the lock; the next call (or reconnect) retries.
                        }

                        int delay = GetRetryDelay(attempt);
                        Console.WriteLine($"Connection attempt {attempt} failed. Retrying in {delay}ms.");
                        await Task.Delay(delay);
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Builds a fresh hub connection with mobile-friendly resilience settings and
        // wires up the reconnect lifecycle handlers. Called once; the instance is reused.
        private void BuildHubConnection()
        {
            string url = GetHubUrl();
            Console.WriteLine($"***Building New Hub Connection*** on {url}");

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(url)
                .WithAutomaticReconnect(new InfiniteRetryPolicy())
                .Build();

            // Tuned for flaky mobile networks: give the server longer to respond before
            // assuming the connection is dead, and send keep-alive pings more often so a
            // genuinely dropped connection is detected (and reconnected) quickly.
            _hubConnection.ServerTimeout = TimeSpan.FromSeconds(60);
            _hubConnection.KeepAliveInterval = TimeSpan.FromSeconds(15);
            _hubConnection.HandshakeTimeout = TimeSpan.FromSeconds(30);

            _hubConnection.Reconnecting += (error) =>
            {
                Console.WriteLine("Reconnecting to the SignalR Hub...");
                OnHubConnecting?.Invoke();
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += async (connectionId) =>
            {
                Console.WriteLine("RECONNECTED to the SignalR Hub.");
                OnHubConnected?.Invoke();
                await Connect(); // Revalidate version and session
            };

            _hubConnection.Closed += (error) =>
            {
                Console.WriteLine("SignalR connection closed." + (error != null ? $" Error: {error.Message}" : ""));
                OnHubDisconnected?.Invoke();

                // Automatic reconnect has been exhausted (or the connection was closed
                // while offline). Re-establish in the background without blocking callers.
                _ = SafeReinitializeAsync();
                return Task.CompletedTask;
            };
        }

        // Waits (polling) until the connection reports Connected or the timeout elapses.
        private async Task WaitForConnectedAsync(TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (_hubConnection != null
                   && _hubConnection.State != HubConnectionState.Connected
                   && DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }
        }

        // Background reconnect used by the Closed handler. Debounced and fully guarded so
        // a reconnect failure can never surface as an unobserved task exception.
        private async Task SafeReinitializeAsync()
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                await Initialize();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Background reconnect failed: {ex.Message}");
            }
        }

        // Retry policy for the built-in automatic reconnect: keep trying forever with a
        // capped backoff so the app self-heals whenever the network returns.
        private sealed class InfiniteRetryPolicy : IRetryPolicy
        {
            public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
                retryContext.PreviousRetryCount switch
                {
                    < 5 => TimeSpan.FromSeconds(1),
                    < 10 => TimeSpan.FromSeconds(3),
                    < 15 => TimeSpan.FromSeconds(5),
                    < 20 => TimeSpan.FromSeconds(10),
                    _ => TimeSpan.FromSeconds(30),
                };
        }


        private int GetRetryDelay(int attempt)
        {
            if (attempt <= 5) return 1000; // 1 second for attempts 1-5
            if (attempt <= 10) return 3000; // 3 seconds for attempts 6-10
            if (attempt <= 15) return 5000; // 5 seconds for attempts 11-15
            if (attempt <= 20) return 10000; // 10 seconds for attempts 16-20
            return 30000; // 30 seconds for attempts beyond 20
        }

        // Optimized Connect which does only one SignalR call for fast loading
        //
        // Connect to the SignalR Server
        // 1. Check Version
        // Store current version in localStorage
        // OnConnect or OnReconnect - get current version and compare with stored version
        // If different, then update
        // 2. Validate the LoginToken, if there is one saved
        // 3. Dump the local log asynchronously if there is one
        private async Task<bool> Connect()
        {
            try
            {

                // Read the locally persisted login Token
                // load the login GUID if there is one in local storage
                _loginGuid = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "loginGuid");

                // CONNECT TO THE SIGNALR HUB
                //  *** CALL THIS EVEN IF _loginGuid is null, so we can get the Version Number, and Photos Base URL!
                string serverVersion = string.Empty;
                try
                {
                    ResponseWrapper<ConnectResult> result = await InvokeHubRawAsync<ResponseWrapper<ConnectResult>>("Connect", _loginGuid);
                    serverVersion = result.Version ?? string.Empty;

                    if (result.IsSuccess)
                    {
                        Util.InitializePhotoBaseUrl(result.Data.PhotosBaseUrl);
                        Console.WriteLine("Photos Base Url: " + result.Data.PhotosBaseUrl);
                        if (result.Data.CurrentUser != null )
                        {
                            _currentUser = result.Data.CurrentUser.Data;
                            if (result.Data.CurrentUser.ErrorCode == 100000)
                            {
                                _currentUser = null;

                                // Invalid login token — RequestLogin() clears the
                                // in-memory field, removes localStorage, and shows
                                // the login prompt.
                                await RequestLogin();
                            }
                        }
                    }
                }
                catch (Exception connectEx)
                {
                    // Connect can throw during client/server version skew (its payload
                    // shape may differ). We still want to detect that skew so downstream
                    // hub errors are suppressed as "refresh available" instead of being
                    // logged as real errors. GetCurrentVersion has the smallest possible
                    // surface for skew, so use it as a fallback.
                    Console.WriteLine($"Connect failed (will still check version): {connectEx.Message}");
                    try { serverVersion = await InvokeHubRawAsync<string>("GetCurrentVersion"); }
                    catch (Exception versionEx) { Console.WriteLine($"GetCurrentVersion fallback failed: {versionEx.Message}"); }
                }


                string previousVersion = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "_currentVersion");
                _currentVersion = string.IsNullOrEmpty(serverVersion) ? null : serverVersion;

                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "_currentVersion", _currentVersion ?? string.Empty);

                if (previousVersion != null && !string.IsNullOrEmpty(_currentVersion) && _currentVersion != previousVersion)
                {
                    // The service worker will pick up the new assets on its next
                    // update check. Show a non-intrusive toast so the user can
                    // refresh at their convenience.
                    OnUpdateAvailable?.Invoke();
                }

                // Detect client/server version skew. Both sides load their own copy
                // of CompanioNation.Shared, so a mismatch means one side is stale.
                // This is exactly what causes hub "parameter mismatch" failures after
                // a partial deploy (e.g. a cached client talking to an older server).
                // Surface the update prompt instead of letting hub calls fail loudly.
                _versionMismatch = !string.IsNullOrEmpty(_currentVersion) &&
                                   !string.Equals(Util.GetCurrentVersion(), _currentVersion, StringComparison.Ordinal);
                if (_versionMismatch)
                {
                    OnUpdateAvailable?.Invoke();
                }

                // Asynchronously dump the local log if there is one
                DumpLocalLog();

                // Validate push subscription on every connect/reconnect for logged-in users.
                // This catches expired or browser-cleared subscriptions and re-registers them.
                if (_currentUser != null && !string.IsNullOrWhiteSpace(_loginGuid))
                {
                    _ = ValidateAndRefreshPushSubscriptionAsync();
                }

                return true;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"REALLY UNEXPECTED Connecting Exception: {ex.Message} {ex.StackTrace}");
                // Log this passively to the local log since we may not be able to connect to the server at this point
                await LogErrorPassive(await BuildErrorDetails("REALLY UNEXPECTED Connecting Exception:", ex, null));

                return false;
            }
        }

        // ──── Resilient hub invocation ────
        //
        // Every ResponseWrapper-returning hub call should go through InvokeHubAsync so
        // that connection setup, transient-drop retries, credential handling and error
        // logging are handled in exactly one place (see the class summary for the rules).

        /// <summary>
        /// Central, resilient wrapper around a hub invocation that returns a
        /// <see cref="ResponseWrapper{T}"/>. It ensures the connection is established,
        /// transparently retries once if the connection went inactive between
        /// <see cref="Initialize"/> and the invoke, treats server timeouts as transient
        /// (logged to the console only, never re-thrown), triggers the login prompt on
        /// an <c>InvalidCredentials</c> result, and logs any unexpected exception to the
        /// server. Callers always receive a non-null wrapper and can rely on
        /// <c>IsSuccess</c>/<c>Data</c>; a failure yields a <see cref="ResponseWrapper{T}.Fail"/>
        /// wrapper whose <c>Data</c> is <c>default</c>.
        /// </summary>
        /// <typeparam name="T">The payload type carried by the response wrapper.</typeparam>
        /// <param name="methodName">The hub method name to invoke.</param>
        /// <param name="args">Arguments to forward to the hub method.</param>
        private async Task<ResponseWrapper<T>> InvokeHubAsync<T>(string methodName, params object?[] args)
        {
            if (_isPrerendering)
                return ResponseWrapper<T>.Fail(ErrorCodes.UnknownError, "Hub unavailable during SSR prerendering.");

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    await Initialize();
                    ResponseWrapper<T> result = await _hubConnection.InvokeCoreAsync<ResponseWrapper<T>>(methodName, args, CancellationToken.None);

                    if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    {
                        await RequestLogin();
                    }

                    return result;
                }
                catch (InvalidOperationException ex) when (attempt == 1)
                {
                    // The connection dropped between Initialize() and the invoke (common on
                    // mobile when the app is backgrounded). Re-initialize and retry once.
                    Console.WriteLine($"Transient connection state in {methodName}; retrying: {ex.Message}");
                }
                catch (TimeoutException ex)
                {
                    // Server did not respond in time — almost always a transient network
                    // drop. Don't spam the server error log; surface a soft failure.
                    Console.WriteLine($"Transient timeout in {methodName}: {ex.Message}");
                    return ResponseWrapper<T>.Fail(ErrorCodes.UnknownError, "The server did not respond. Please try again.");
                }
                catch (HttpRequestException ex)
                {
                    // Browser-level network failure (e.g., "TypeError: Failed to fetch").
                    // Transient — treat the same as a timeout.
                    Console.WriteLine($"Transient network error in {methodName}: {ex.Message}");
                    return ResponseWrapper<T>.Fail(ErrorCodes.UnknownError, "The server did not respond. Please try again.");
                }
                catch (WebSocketException ex)
                {
                    // WebSocket transport broken (e.g. connection dropped mid-message).
                    // Transient — treat the same as a timeout.
                    Console.WriteLine($"Transient WebSocket error in {methodName}: {ex.Message}");
                    return ResponseWrapper<T>.Fail(ErrorCodes.UnknownError, "The server did not respond. Please try again.");
                }
                catch (HubException ex)
                {
                    // Our hub methods return ResponseWrapper and never throw, so a
                    // HubException here is almost always client/server version skew
                    // (a parameter signature mismatch after a partial deploy). When we
                    // already detected the skew, surface the update prompt and skip the
                    // error report rather than spamming it for a known transient state.
                    if (_versionMismatch)
                    {
                        Console.WriteLine($"Version-skew hub error in {methodName}: {ex.Message}");
                        OnUpdateAvailable?.Invoke();
                        return ResponseWrapper<T>.Fail(ErrorCodes.UnknownError, "A new version is available. Please refresh to continue.");
                    }

                    await LogError(ex, $"{methodName}()");
                    return ResponseWrapper<T>.Fail(ErrorCodes.UnknownError, ex.Message);
                }
                catch (Exception ex)
                {
                    await LogError(ex, $"{methodName}()");
                    return ResponseWrapper<T>.Fail(ErrorCodes.UnknownError, ex.Message);
                }
            }

            // Unreachable in practice (the loop always returns), but keeps callers safe.
            return ResponseWrapper<T>.Fail(ErrorCodes.UnknownError, "Unable to reach the server.");
        }

        /// <summary>
        /// Resilient wrapper for a "fire-and-forget"-style hub method that returns no
        /// payload. Shares the same connect/retry/timeout handling as
        /// <see cref="InvokeHubAsync{T}"/>; failures are swallowed after logging so a
        /// missed non-critical update (e.g. a badge count) never breaks the UI.
        /// </summary>
        /// <param name="methodName">The hub method name to invoke.</param>
        /// <param name="args">Arguments to forward to the hub method.</param>
        private async Task InvokeHubVoidAsync(string methodName, params object?[] args)
        {
            if (_isPrerendering) return;

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    await Initialize();
                    await _hubConnection.InvokeCoreAsync(methodName, typeof(object), args, CancellationToken.None);
                    return;
                }
                catch (InvalidOperationException ex) when (attempt == 1)
                {
                    Console.WriteLine($"Transient connection state in {methodName}; retrying: {ex.Message}");
                }
                catch (TimeoutException ex)
                {
                    Console.WriteLine($"Transient timeout in {methodName}: {ex.Message}");
                    return;
                }
                catch (HttpRequestException ex)
                {
                    // Browser-level network failure (e.g., "TypeError: Failed to fetch").
                    // Transient — treat the same as a timeout.
                    Console.WriteLine($"Transient network error in {methodName}: {ex.Message}");
                    return;
                }
                catch (WebSocketException ex)
                {
                    // WebSocket transport broken (e.g. connection dropped mid-message).
                    // Transient — treat the same as a timeout.
                    Console.WriteLine($"Transient WebSocket error in {methodName}: {ex.Message}");
                    return;
                }
                catch (HubException ex)
                {
                    if (_versionMismatch)
                    {
                        Console.WriteLine($"Version-skew hub error in {methodName}: {ex.Message}");
                        OnUpdateAvailable?.Invoke();
                        return;
                    }

                    await LogError(ex, $"{methodName}()");
                    return;
                }
                catch (Exception ex)
                {
                    await LogError(ex, $"{methodName}()");
                    return;
                }
            }
        }

        /// <summary>
        /// Resilient wrapper for hand-rolled hub calls that need custom error-code
        /// processing. Handles ONLY the connection-drop retry (same pattern as
        /// <see cref="InvokeHubAsync{T}"/>). All other exceptions propagate to the
        /// caller's catch block so custom logic (subscription prompts, tuple returns,
        /// error-code mapping) stays in one place.
        /// </summary>
        /// <typeparam name="T">The return type (usually <see cref="ResponseWrapper{T}"/>).</typeparam>
        /// <param name="methodName">The hub method name to invoke.</param>
        /// <param name="args">Arguments to forward to the hub method.</param>
        private async Task<T> InvokeHubRawAsync<T>(string methodName, params object?[] args)
        {
            if (_isPrerendering)
                throw new InvalidOperationException("Cannot invoke hub methods during SSR prerendering.");

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await Initialize();
                    return await _hubConnection.InvokeCoreAsync<T>(methodName, args, CancellationToken.None);
                }
                catch (InvalidOperationException ex) when (attempt == 1)
                {
                    // Connection dropped during a long-running operation (e.g. JS interop
                    // for photo processing). Auto-reconnect is already in progress — give
                    // it time to complete, then retry.
                    Console.WriteLine($"Transient connection state in {methodName}; retrying: {ex.Message}");
                    await Task.Delay(8000);
                }
                catch (HttpRequestException ex) when (attempt <= 2)
                {
                    // Browser-level network failure (e.g., "TypeError: Failed to fetch").
                    // Transient — delay and retry a couple of times, then let the caller
                    // handle it with its custom error logic.
                    Console.WriteLine($"Transient network error in {methodName} (attempt {attempt}): {ex.Message}");
                    await Task.Delay(3000);
                }
                catch (WebSocketException ex) when (attempt <= 2)
                {
                    // WebSocket transport broken (e.g. connection dropped mid-message).
                    // Transient — delay and retry, then let the caller handle it.
                    Console.WriteLine($"Transient WebSocket error in {methodName} (attempt {attempt}): {ex.Message}");
                    await Task.Delay(3000);
                }
            }
        }

        /// <summary>Sends the current push-notification token to the server for this login.</summary>
        public async Task UpdatePushToken(string pushToken)
        {
            try
            {
                Console.WriteLine($"[Push] UpdatePushToken: sending token to server ({pushToken?.Length ?? 0} chars).");
                ResponseWrapper<bool> result = await InvokeHubRawAsync<ResponseWrapper<bool>>("UpdatePushToken", _loginGuid, pushToken);
                Console.WriteLine($"[Push] UpdatePushToken result: success={result?.IsSuccess}, message={result?.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Push] UpdatePushToken failed: {ex.Message}");
                await LogError(ex);
            }
        }

        /// <summary>
        /// Validates that the push subscription is still active and re-registers if needed.
        /// For native iOS apps, reads the FCM token. For web, validates the VAPID subscription.
        /// Safe to call on every connect/reconnect — it only re-subscribes when the subscription is missing.
        /// </summary>
        private async Task ValidateAndRefreshPushSubscriptionAsync()
        {
            try
            {
                Console.WriteLine("[Push] ValidateAndRefreshPushSubscriptionAsync: validating push subscription...");
                string pushToken = await GetPushTokenAsync();
                // IMPORTANT: only send a genuinely non-empty token. On native iOS the FCM device
                // token arrives ASYNCHRONOUSLY (after APNs registration completes), so at connect
                // time GetPushTokenAsync often returns "" ("not ready yet"). Writing that empty
                // string would blank out the DB push token — the real token is delivered shortly
                // after via the OnFcmTokenChanged native callback, which forwards it to the server.
                if (!string.IsNullOrWhiteSpace(pushToken))
                {
                    Console.WriteLine($"[Push] Push token obtained ({pushToken.Length} chars) — sending to server.");
                    await UpdatePushToken(pushToken);
                }
                else
                {
                    Console.WriteLine("[Push] GetPushTokenAsync returned null/empty — push registration skipped (token not ready yet).");
                    // Empty/null is normal in two cases: (a) the user hasn't granted permission yet,
                    // or (b) native iOS where the FCM token hasn't arrived yet (the async
                    // OnFcmTokenChanged callback will send the real token when it lands).
                    // Only alert when permission IS granted AND we are NOT on native iOS — that
                    // combination indicates a genuine web subscription/registration failure that
                    // needs immediate attention. On native iOS a not-ready token is expected, so we
                    // must not spam an alert while waiting for the async token.
                    bool isNativeIos = await IsNativeIosAppAsync();
                    if (!isNativeIos && await IsPushPermissionGrantedAsync())
                    {
                        await LogError("[Push] Push registration FAILED despite notification permission being GRANTED — this user will not receive push notifications. Investigate immediately.");
                    }
                    else if (isNativeIos && await IsPushPermissionGrantedAsync())
                    {
                        // Native iOS with granted permission but no token yet is normally just the
                        // async FCM token not having arrived. But if it NEVER arrives, that's a silent
                        // showstopper. Start a one-shot delayed watchdog that re-checks and only emails
                        // an alert if the token is STILL missing after a reasonable window.
                        StartNativeIosPushTokenWatchdog();
                    }
                }
            }
            catch (Exception ex)
            {
                // Push validation is best-effort; don't block the connection flow
                Console.WriteLine($"[Push] Push subscription validation failed: {ex.Message}");
                await LogError("[Push] Push subscription validation threw an unexpected exception — user may not receive push notifications.", ex, null);
            }
        }

        // Guards against multiple overlapping iOS token watchdogs (e.g. from reconnect churn).
        private int _nativeIosPushWatchdogRunning;

        /// <summary>
        /// One-shot delayed watchdog for the native iOS "FCM token never arrives" showstopper.
        /// On native iOS the FCM token arrives asynchronously after a granted permission (via the
        /// OnFcmTokenChanged callback). If it never arrives, the user silently gets no push
        /// notifications. This waits ~45s and, if the token is STILL empty while permission remains
        /// granted, emails an alert. Fire-and-forget; guarded so only one runs at a time.
        /// </summary>
        private void StartNativeIosPushTokenWatchdog()
        {
            // Ensure only a single watchdog is in flight at any time.
            if (Interlocked.CompareExchange(ref _nativeIosPushWatchdogRunning, 1, 0) != 0)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(45));

                    // Re-check current state: the async OnFcmTokenChanged callback may have
                    // delivered the real token in the meantime, in which case there's nothing wrong.
                    if (!await IsNativeIosAppAsync() || !await IsPushPermissionGrantedAsync())
                    {
                        return;
                    }

                    string token = await GetPushTokenAsync();
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        await LogError("[Push] SHOWSTOPPER: native iOS notification permission is GRANTED but no FCM push token arrived after 45s — this user will receive NO push notifications. Likely an APNs/FCM registration failure in the native wrapper. Investigate immediately.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Push] iOS token watchdog error: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _nativeIosPushWatchdogRunning, 0);
                }
            });
        }

        /// <summary>
        /// Returns true when the user has actually granted push-notification permission
        /// (web: Notification.permission === 'granted'; native iOS: authorized/ephemeral/provisional).
        /// Used to distinguish a genuine registration failure from the normal "not opted in yet" state.
        /// </summary>
        private async Task<bool> IsPushPermissionGrantedAsync()
        {
            try
            {
                return await _jsRuntime.InvokeAsync<bool>("window.isPushPermissionGranted");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true when running inside the native iOS app wrapper (WKWebView), where the
        /// FCM push token arrives asynchronously and an empty token at connect time is expected.
        /// </summary>
        private async Task<bool> IsNativeIosAppAsync()
        {
            try
            {
                return await _jsRuntime.InvokeAsync<bool>("window.isNativeIosApp");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the push token for the current device.
        /// For native iOS apps (WKWebView wrapper), returns the FCM device token.
        /// For web browsers, validates/creates a VAPID Web Push subscription and returns it as JSON.
        /// Returns empty string when native iOS is detected but the FCM token isn't ready yet,
        /// so callers clear the stale DB token (e.g. a VAPID token from a previous device).
        /// The FCM callback (OnFcmTokenChanged) will write the real token when it arrives.
        /// </summary>
        private async Task<string> GetPushTokenAsync()
        {
            try
            {
                bool isNative = await _jsRuntime.InvokeAsync<bool>("window.isNativeIosApp");
                if (isNative)
                {
                    Console.WriteLine("[Push] GetPushTokenAsync: native iOS detected, reading FCM token.");
                    string fcmToken = await _jsRuntime.InvokeAsync<string>("window.getFcmToken");
                    Console.WriteLine($"[Push] FCM token: {(fcmToken is not null ? $"{fcmToken.Length} chars" : "not ready (null) — returning empty to clear stale token")}");
                    // Return "" (not null) when FCM token isn't ready yet so callers
                    // clear any stale token from a different device (e.g. Android → iPhone switch).
                    return fcmToken ?? "";
                }

                Console.WriteLine("[Push] GetPushTokenAsync: web browser detected, validating VAPID subscription.");
                string token = await _jsRuntime.InvokeAsync<string>("window.validatePushSubscription", Util.VapidPublicKey);
                Console.WriteLine($"[Push] VAPID subscription result: {(token is not null ? $"{token.Length} chars" : "null")}");
                return token;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Push] GetPushTokenAsync failed: {ex.Message}");
                await LogError("[Push] GetPushTokenAsync threw while obtaining the push token — user will not receive push notifications.", ex, null);
                return null;
            }
        }

        private async Task AppendToLocalLog(DateTime i_timestamp, string i_message, string i_version)
        {
            // During SSR prerendering, _jsRuntime is unavailable. Skip silently.
            if (_isPrerendering) return;

            try
            {
                // Retrieve the log entries from localStorage
                string logEntriesJson = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "errorLog");
                List<LogEntry> logEntries = string.IsNullOrWhiteSpace(logEntriesJson)
                    ? new List<LogEntry>()
                    : JsonSerializer.Deserialize<List<LogEntry>>(logEntriesJson);

                // Create a new log entry
                LogEntry newLog = new LogEntry
                {
                    timestamp = i_timestamp,
                    message = i_message,
                    version = i_version
                };

                // Append the new log entry to the list
                logEntries.Add(newLog);

                // Serialize the updated list back to JSON
                string updatedLogEntriesJson = JsonSerializer.Serialize(logEntries);

                // Store the updated log entries in localStorage
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "errorLog", updatedLogEntriesJson);
            }
            catch (Exception ex)
            {
                // There's not much we can do if we get an error here, so just log it to the console
                Console.Error.WriteLine(ex.Message + ex.StackTrace);
            }
        }

        private async Task DumpLocalLog()
        {
            try
            {
                string logEntriesJson = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "errorLog");
                if (string.IsNullOrWhiteSpace(logEntriesJson)) return;

                List<LogEntry> logEntries = JsonSerializer.Deserialize<List<LogEntry>>(logEntriesJson);
                if (logEntries == null || logEntries.Count == 0) return;

                int totalEntries = logEntries.Count;
                bool isFirst = true;

                while (logEntries.Count > 0)
                {
                    string message = logEntries[0].message;

                    // Tag the first entry so the recipient knows this is a local log dump.
                    // This replaces the separate "DUMPING"/"COMPLETE" marker messages
                    // that each consumed an email slot.
                    if (isFirst)
                    {
                        message = $"====== LOCAL LOG DUMP ({totalEntries} {(totalEntries == 1 ? "entry" : "entries")}) ======\n{message}";
                        isFirst = false;
                    }

                    await _hubConnection.InvokeAsync("LogError", Util.ClientLogSchema, logEntries[0].timestamp, message, logEntries[0].version);
                    logEntries.RemoveAt(0);
                    string updatedLogEntriesJson = JsonSerializer.Serialize(logEntries);
                    await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "errorLog", updatedLogEntriesJson);
                }

                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "errorLog");
            }
            catch (Exception ex)
            {
                await AppendToLocalLog(DateTime.UtcNow, ex.Message + ex.StackTrace, Util.GetCurrentVersion());
            }
        }

        private string? _clientInfo;
        private bool _clientInfoLoaded;

        private async Task EnsureClientInfoAsync()
        {
            if (_clientInfoLoaded) return;

            var parts = new List<string>();
            try
            {
                // Each property is captured independently so a single unsupported API
                // (e.g. userAgentData in older Safari) can never wipe out the rest.
                async Task<string> CaptureAsync(string label, string expression)
                {
                    try
                    {
                        var value = await _jsRuntime.InvokeAsync<string>("eval", expression);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return $"{label}: {value}";
                        }
                    }
                    catch
                    {
                        // Best-effort: skip this property if the browser rejects it.
                    }
                    return string.Empty;
                }

                var userAgent = await CaptureAsync("UA", "navigator.userAgent ?? ''");
                var platform = await CaptureAsync("Platform", "navigator.userAgentData?.platform ?? navigator.platform ?? ''");
                var vendor = await CaptureAsync("Vendor", "navigator.vendor ?? ''");
                var languages = await CaptureAsync("Languages", "navigator.languages ? navigator.languages.join(',') : (navigator.language ?? '')");
                var userAgentData = await CaptureAsync("UAData", "navigator.userAgentData ? JSON.stringify(navigator.userAgentData) : ''");
                var screen = await CaptureAsync("Screen", "window.screen ? `${screen.width}x${screen.height} @${window.devicePixelRatio || 1}x dpr` : ''");
                var viewport = await CaptureAsync("Viewport", "`${window.innerWidth}x${window.innerHeight}`");
                var orientation = await CaptureAsync("Orientation", "screen.orientation?.type ?? ''");
                var timeZone = await CaptureAsync("TimeZone", "Intl.DateTimeFormat().resolvedOptions().timeZone ?? ''");
                var online = await CaptureAsync("Network", "navigator.onLine ? 'online' : 'offline'");
                var connection = await CaptureAsync("Connection", "navigator.connection ? `${navigator.connection.effectiveType || 'n/a'} (${navigator.connection.downlink || '?'} Mbps, rtt ${navigator.connection.rtt || '?'} ms)` : ''");
                var displayMode = await CaptureAsync("DisplayMode", "(window.matchMedia('(display-mode: standalone)').matches || window.matchMedia('(display-mode: fullscreen)').matches || window.matchMedia('(display-mode: minimal-ui)').matches || window.navigator.standalone === true) ? 'standalone/pwa' : 'browser-tab'");
                var nativeIos = await CaptureAsync("NativeShell", "typeof window.isNativeIosApp === 'function' && window.isNativeIosApp() ? 'apple' : ''");
                var nativeVersion = await CaptureAsync("NativeAppVersion", "typeof window.nativeAppVersion === 'string' && window.nativeAppVersion.length > 0 ? window.nativeAppVersion : ''");
                var memory = await CaptureAsync("DeviceMemory", "navigator.deviceMemory ? `${navigator.deviceMemory} GB` : ''");
                var cores = await CaptureAsync("HardwareConcurrency", "navigator.hardwareConcurrency ? `${navigator.hardwareConcurrency} cores` : ''");

                parts.AddRange(new[] { userAgent, platform, vendor, languages, userAgentData, screen, viewport, orientation, timeZone, online, connection, displayMode, nativeIos, nativeVersion, memory, cores }.Where(p => !string.IsNullOrWhiteSpace(p)));
                _clientInfo = string.Join("; ", parts);
            }
            catch (Exception ex)
            {
                _clientInfo = $"UA capture failed: {ex.Message}";
            }
            finally
            {
                _clientInfoLoaded = true;
            }
        }

        private async Task<string> BuildErrorDetails(string message, Exception? exception, string? additionalInfo)
        {
            await EnsureClientInfoAsync();

            var sb = new StringBuilder();
            var baseMessage = string.IsNullOrWhiteSpace(message) ? "Unexpected client error" : message;
            sb.AppendLine(baseMessage);

            if (!string.IsNullOrWhiteSpace(additionalInfo))
            {
                sb.AppendLine($"AdditionalInfo: {additionalInfo}");
            }

            if (!string.IsNullOrWhiteSpace(_clientInfo))
            {
                sb.AppendLine($"Client: {_clientInfo}");
            }

            sb.AppendLine($"ClientVersion: {Util.GetCurrentVersion()}");
            sb.AppendLine($"ServerVersion: {_currentVersion ?? "unknown"}");
            sb.AppendLine($"HubState: {_hubConnection?.State}");
            sb.AppendLine($"HubConnectionId: {_hubConnection?.ConnectionId ?? "null"}");
            sb.AppendLine($"NavigationUri: {_navigationManager.Uri}");
            sb.AppendLine($"LoginGuidPresent: {!string.IsNullOrWhiteSpace(_loginGuid)}");
            sb.AppendLine($"HasUser: {_currentUser != null}");
            sb.AppendLine($"UserId: {_currentUser?.UserId.ToString() ?? "not-logged-in"}");
            sb.AppendLine($"Email: {(string.IsNullOrWhiteSpace(_currentUser?.Email) ? "not-logged-in" : _currentUser.Email)}");
            sb.AppendLine($"TimestampLocal: {DateTime.Now:O}");
            sb.AppendLine($"TimestampUtc: {DateTime.UtcNow:O} (Vancouver: {Util.FormatVancouverTime(DateTime.UtcNow)})");

            if (exception != null)
            {
                sb.AppendLine($"ExceptionType: {exception.GetType().FullName}");
                sb.AppendLine($"HResult: {exception.HResult}");
                sb.AppendLine($"Message: {exception.Message}");
                sb.AppendLine($"StackTrace: {exception.StackTrace}");

                if (exception.InnerException != null)
                {
                    sb.AppendLine("-- Inner Exception --");
                    sb.AppendLine($"InnerExceptionType: {exception.InnerException.GetType().FullName}");
                    sb.AppendLine($"InnerMessage: {exception.InnerException.Message}");
                    sb.AppendLine($"InnerStackTrace: {exception.InnerException.StackTrace}");
                }
            }

            return sb.ToString();
        }

        public async Task LogError<T>(ResponseWrapper<T> error)
        {
            await LogError($"({error.ErrorCode} 0x{error.ErrorCode:X8}) {error.Message}");
        }
        public async Task LogError(Exception i_ex)
        {
            await LogError(i_ex, null);
        }

        public async Task LogError(Exception i_ex, string? i_additionalInfo)
        {
            await LogError("Client exception", i_ex, i_additionalInfo);
        }
        public async Task LogError(string i_message)
        {
            await LogError(i_message, null, null);
        }
        public async Task LogError(string i_message, Exception? i_ex, string? i_additionalInfo)
        {
            var formatted = await BuildErrorDetails(i_message, i_ex, i_additionalInfo);
            try
            {
                await Initialize();
                await _hubConnection.InvokeAsync("LogError", Util.ClientLogSchema, DateTime.UtcNow, formatted, Util.GetCurrentVersion());
            }
            catch (Exception ex)
            {
                await LogErrorPassive(formatted);
                await LogErrorPassive(await BuildErrorDetails("Failed to send log to server", ex, null));
            }
        }

        public async Task LogClientError(ClientErrorReport errorReport)
        {
            if (errorReport == null)
            {
                return;
            }

            errorReport.UserId ??= _currentUser?.UserId;
            errorReport.Route ??= _navigationManager.Uri;
            errorReport.AppVersion ??= Util.GetCurrentVersion();

            try
            {
                await Initialize();
                await _hubConnection.InvokeAsync("LogClientError", Util.ClientLogSchema, errorReport);
            }
            catch (Exception ex)
            {
                await LogErrorPassive(await BuildErrorDetails("Failed to send client error report", ex, JsonSerializer.Serialize(errorReport)));
            }
        }

        public async Task LogErrorPassive(string i_message)
        {
            await AppendToLocalLog(DateTime.UtcNow, i_message, Util.GetCurrentVersion());
        }


        public async Task SetMessageCount(int messageCount)
        {
            if (_currentUser != null)
            {
                _currentUser.UnreadMessagesCount = messageCount;
                OnStateHasChanged?.Invoke();  // Make sure the UI is updated with the new unread message count
            }
        }

        /// <summary>Returns the current contest leaderboard, or null if the call fails.</summary>
        public async Task<List<Companion>> GetContestLeaderBoard()
        {
            ResponseWrapper<List<Companion>> result = await InvokeHubAsync<List<Companion>>("GetContestLeaderBoard");
            return result.IsSuccess ? result.Data : null;
        }

        /// <summary>Returns a single CompanioNita advice entry by id, or null if the call fails.</summary>
        public async Task<CompanioNitaAdvice> GetCompanionitaAdviceById(int adviceId)
        {
            ResponseWrapper<CompanioNitaAdvice> result = await InvokeHubAsync<CompanioNitaAdvice>("GetCompanioNitaAdviceById", adviceId, CultureService.GetCurrentCulture());
            return result.IsSuccess ? result.Data : null;
        }

        /// <summary>Returns a page of CompanioNita advice entries, or null if the call fails.</summary>
        public async Task<List<CompanioNitaAdvice>> GetCompanionitaAdvice(int start, int count)
        {
            ResponseWrapper<List<CompanioNitaAdvice>> result = await InvokeHubAsync<List<CompanioNitaAdvice>>("GetCompanioNitaAdvice", start, count, CultureService.GetCurrentCulture());
            return result.IsSuccess ? result.Data : null;
        }

        /// <summary>
        /// Streams CompanioNita's insight into a conversation, invoking the callback with
        /// the accumulated text after each chunk. Returns the full response when the
        /// stream completes; an ⚠️-prefixed message on subscription limits, or a friendly
        /// error string on failure. Unlike the non-streaming variant, the UI stays fully
        /// responsive while the server generates.
        /// </summary>
        public async Task<string> StreamAskCompanioNitaAboutConversationAsync(int userId, Action<string> onChunkReceived, Action<string>? onReasoningReceived = null)
        {
            try
            {
                await Initialize();
                var fullResponse = new StringBuilder();
                var reasoning = new StringBuilder();

                await foreach (string chunk in _hubConnection.StreamAsync<string>("StreamAskCompanioNitaAboutConversation", _loginGuid, userId))
                {
                    // Check for error marker (subscription/validation errors from server)
                    if (chunk.Length > 0 && chunk[0] == '\u0001')
                    {
                        string errorInfo = chunk[1..];
                        int colonIdx = errorInfo.IndexOf(':');
                        if (colonIdx > 0 && int.TryParse(errorInfo[..colonIdx], out int errorCode))
                        {
                            if (errorCode == ErrorCodes.InvalidCredentials)
                            {
                                await RequestLogin();
                                return "";
                            }
                            if (IsSubscriptionError(errorCode))
                            {
                                RequestSubscription();
                                return $"⚠️ {errorInfo[(colonIdx + 1)..]}";
                            }
                            // Unrecognized error code (e.g. content violation): surface the
                            // human-readable message without the numeric prefix.
                            return $"⚠️ {errorInfo[(colonIdx + 1)..]}";
                        }
                        return $"⚠️ {errorInfo}";
                    }

                    // Reasoning chunk (display-only): accumulate it into a line-aligned
                    // tail so the "thinking" text reads like flowing prose with each
                    // completed line staying in place. It never enters the visible response.
                    if (chunk.Length > 0 && chunk[0] == '\u0002')
                    {
                        reasoning.Append(chunk[1..]);
                        SafeInvoke(onReasoningReceived, FormatReasoningForDisplay(reasoning));
                        continue;
                    }

                    fullResponse.Append(chunk);
                    SafeInvoke(onChunkReceived, fullResponse.ToString());
                }

                return fullResponse.ToString();
            }
            catch (Exception ex)
            {
                await LogError(ex);
                return "CompanioNita is having trouble right now. Please try again in a moment.";
            }
        }
        /// <summary>Sends a message to CompanioNita and returns its reply; an ⚠️-prefixed message on subscription limits, or an ERROR string on failure.</summary>
        public async Task<string> AskCompanioNita(int threadId, string i_message)
        {
            try
            {
                await Initialize();
                ResponseWrapper<string> result = await InvokeHubRawAsync<ResponseWrapper<string>>("AskCompanioNita", _loginGuid, threadId, i_message);

                if (!result.IsSuccess)
                {
                    if (result.ErrorCode == ErrorCodes.InvalidCredentials)
                    {
                        await RequestLogin();
                    }
                    else if (IsSubscriptionError(result.ErrorCode))
                    {
                        // Subscription required, expired, inactive, or usage limit exceeded
                        RequestSubscription();
                        return $"⚠️ {result.Message}";
                    }
                }

                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex);
                return "CompanioNita is having trouble right now. Please try again in a moment.";
            }
        }

        /// <summary>
        /// Streams CompanioNita's response, invoking the callback with the accumulated text after each chunk.
        /// Returns the full response when the stream completes.
        /// </summary>
        public async Task<string> StreamAskCompanioNitaAsync(int threadId, string i_message, Action<string> onChunkReceived, Action<string>? onReasoningReceived = null)
        {
            try
            {
                await Initialize();
                var fullResponse = new StringBuilder();
                var reasoning = new StringBuilder();

                await foreach (string chunk in _hubConnection.StreamAsync<string>("StreamAskCompanioNita", _loginGuid, threadId, i_message))
                {
                    // Check for error marker (subscription/validation errors from server)
                    if (chunk.Length > 0 && chunk[0] == '\u0001')
                    {
                        string errorInfo = chunk[1..];
                        int colonIdx = errorInfo.IndexOf(':');
                        if (colonIdx > 0 && int.TryParse(errorInfo[..colonIdx], out int errorCode))
                        {
                            if (errorCode == ErrorCodes.InvalidCredentials)
                            {
                                await RequestLogin();
                                return "";
                            }
                            if (IsSubscriptionError(errorCode))
                            {
                                RequestSubscription();
                                return $"⚠️ {errorInfo[(colonIdx + 1)..]}";
                            }
                            // Unrecognized error code (e.g. content violation): surface the
                            // human-readable message without the numeric prefix.
                            return $"⚠️ {errorInfo[(colonIdx + 1)..]}";
                        }
                        return $"⚠️ {errorInfo}";
                    }

                    // Reasoning chunk (display-only): accumulate it into a line-aligned
                    // tail so the "thinking" text reads like flowing prose with each
                    // completed line staying in place. It never enters the visible response.
                    if (chunk.Length > 0 && chunk[0] == '\u0002')
                    {
                        reasoning.Append(chunk[1..]);
                        SafeInvoke(onReasoningReceived, FormatReasoningForDisplay(reasoning));
                        continue;
                    }

                    fullResponse.Append(chunk);
                    SafeInvoke(onChunkReceived, fullResponse.ToString());
                }

                return fullResponse.ToString();
            }
            catch (Exception ex)
            {
                await LogError(ex);
                return "CompanioNita is having trouble right now. Please try again in a moment.";
            }
        }

        /// <summary>
        /// Invokes a streaming callback without allowing a thrown exception (e.g. from a
        /// page component that was disposed when the user navigated away) to abort the
        /// stream. Generation must keep running to completion so the server can persist
        /// the response for when the user returns.
        /// </summary>
        private static void SafeInvoke(Action<string>? callback, string value)
        {
            try
            {
                callback?.Invoke(value);
            }
            catch
            {
                // Best-effort UI notification; never let it stop the stream.
            }
        }

        // Reasoning deltas arrive as tiny fragments (often a single word), so the
        // "thinking" indicator accumulates them and shows a bounded, line-aligned tail
        // instead of overwriting in place with each fragment (which reads as an
        // unreadable marquee). Completed lines stay in place while the newest line
        // continues to build.
        private const int ReasoningTailMaxChars = 1200;

        private static string FormatReasoningForDisplay(StringBuilder reasoning)
        {
            // The model may reason about (or emit) HTML while working toward an HTML
            // answer; never show raw markup in the thinking indicator.
            string text = Util.StripHtmlTags(reasoning.ToString());

            if (text.Length <= ReasoningTailMaxChars)
                return text;

            // Drop only whole lines from the top so the visible window always begins
            // at a line boundary rather than cutting a sentence mid-word.
            string tail = text[^ReasoningTailMaxChars..];
            int newline = tail.IndexOf('\n');
            return newline > 0 && newline < tail.Length - 1
                ? tail[(newline + 1)..]
                : tail;
        }

        /// <summary>Creates a new CompanioNita advice thread; returns its id, or 0 on failure.</summary>
        public async Task<int> StartAdviceThreadAsync()
        {
            try
            {
                await Initialize();
                ResponseWrapper<int> result = await InvokeHubRawAsync<ResponseWrapper<int>>("StartAdviceThread", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
                    await RequestLogin();
                }
                return result.IsSuccess ? result.Data : 0;
            }
            catch (Exception ex)
            {
                await LogError(ex);
                return 0;
            }
        }

        /// <summary>Returns the caller's CompanioNita advice threads, newest first; empty list on failure.</summary>
        public async Task<List<AdviceThread>> GetAdviceThreadsAsync()
        {
            ResponseWrapper<List<AdviceThread>> result = await InvokeHubAsync<List<AdviceThread>>("GetAdviceThreads", _loginGuid);
            return result.IsSuccess ? result.Data ?? [] : [];
        }

        /// <summary>Returns the question/answer exchanges of one CompanioNita advice thread, oldest first; empty list on failure.</summary>
        public async Task<List<AdviceExchange>> GetAdviceExchangesAsync(int threadId)
        {
            try
            {
                await Initialize();
                ResponseWrapper<List<AdviceExchange>> result = await InvokeHubRawAsync<ResponseWrapper<List<AdviceExchange>>>("GetAdviceExchanges", _loginGuid, threadId);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
                    await RequestLogin();
                }
                return result.IsSuccess ? result.Data ?? [] : [];
            }
            catch (Exception ex)
            {
                await LogError(ex);
                return [];
            }
        }

        /// <summary>Persists the active advice-thread id so a refresh resumes the same thread. Best-effort.</summary>
        public async Task SaveActiveThreadIdAsync(int threadId)
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "activeAdviceThreadId", threadId.ToString());
            }
            catch
            {
                // Best-effort — localStorage may be unavailable during prerendering.
            }
        }

        /// <summary>Returns the persisted active advice-thread id, or 0 when none/unavailable.</summary>
        public async Task<int> GetActiveThreadIdAsync()
        {
            try
            {
                string? stored = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "activeAdviceThreadId");
                return int.TryParse(stored, out int id) ? id : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Re-fetches the current user from the server so cached profile fields (e.g.
        /// subscription expiry) reflect a just-completed purchase. Used after the subscribe
        /// popup closes; <see cref="Initialize"/> alone does not refresh an already-connected session.
        /// </summary>
        public async Task RefreshCurrentUserAsync()
        {
            if (_isPrerendering) return;
            try
            {
                await Initialize();
                await Connect();
            }
            catch (Exception ex)
            {
                await LogError(ex, "RefreshCurrentUserAsync");
            }
        }

        /// <summary>Returns the personalized advice list for the current user (prompts login if the session is invalid).</summary>
        public async Task<List<Advice>> GetAdvice()
        {
            ResponseWrapper<List<Advice>> result = await InvokeHubAsync<List<Advice>>("GetAdvice", _loginGuid);
            return result.Data;
        }

        /// <summary>Clears the local session and login token, stops push notifications, and unregisters the push subscription.</summary>
        public async Task Logout()
        {
            try
            {
                _currentUser = null;
                _loginGuid = null;

                await Initialize();
                await UpdatePushToken(""); // clear the push token so that we don't inadvertently keep sending push notifications

                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "loginGuid");

                // We don't want to keep getting notifications after the user logged out, someone unauthorized could see them
                await _jsRuntime.InvokeVoidAsync("window.unregisterPush");
            }
            catch (Exception ex)
            {
                await LogError(ex);
            }
        }


        /// <summary>Returns the current user's settings (prompts login if the session is invalid).</summary>
        public async Task<Settings> GetSettingsAsync()
        {
            ResponseWrapper<Settings> result = await InvokeHubAsync<Settings>("GetSettings", CultureService.GetCurrentCulture());
            return result.Data;
        }

        private async Task DoLogin(ResponseWrapper<UserDetails> loginResult)
        {
            _currentUser = loginResult.Data;

            if (loginResult.IsSuccess)
            {
                // successful login
                _loginGuid = loginResult.Data.LoginToken?.ToString();  // Ensure safe access with '?' to avoid null reference exceptions
                if (!string.IsNullOrWhiteSpace(_loginGuid))
                {
                    // send the push token to the server (FCM for native iOS, VAPID for web).
                    // Only send a genuinely non-empty token: on native iOS the FCM token arrives
                    // asynchronously after login, so GetPushTokenAsync returns "" here ("not ready
                    // yet"). Writing that empty string would store a blank push token in the DB.
                    // The real token is delivered shortly after via the OnFcmTokenChanged native
                    // callback, which forwards it to the server.
                    //
                    // On web/Android, an empty result usually means the service worker isn't
                    // .ready yet (fresh install / first login race). Retry a few times so the new
                    // device's subscription DOES land in the DB and displaces the prior device's
                    // token — otherwise notifications continue firing on the old device.
                    string pushToken = await GetPushTokenAsync();
                    if (string.IsNullOrWhiteSpace(pushToken)
                        && !await IsNativeIosAppAsync()
                        && await IsPushPermissionGrantedAsync())
                    {
                        for (int attempt = 1; attempt <= 3 && string.IsNullOrWhiteSpace(pushToken); attempt++)
                        {
                            Console.WriteLine($"[Push] DoLogin: web/Android permission granted but no token yet; retry {attempt}/3 after delay.");
                            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
                            pushToken = await GetPushTokenAsync();
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(pushToken))
                    {
                        await UpdatePushToken(pushToken);
                    }
                }
                else
                {
                    _loginGuid = null;
                }
            }
            else if (loginResult.ErrorCode == 100000)
            {
                // invalid login credentials
                _loginGuid = null;
            }
            else
            {
                // some other error
                _loginGuid = null;
                await LogError($"Login() error code {loginResult.ErrorCode}");
            }

            // Persist the authentication GUID in local storage if available
            if (!string.IsNullOrWhiteSpace(_loginGuid))
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "loginGuid", _loginGuid);
            }
            else
            {
                await Logout();
            }

            // The layout is long-lived across client-side navigation, so notify it
            // immediately that the auth state changed (login or logout).
            OnStateHasChanged?.Invoke();
        }
        public async Task<ResponseWrapper<UserDetails>> Login(string i_email, string i_password)
        {
            try
            {
                await Initialize();

                ResponseWrapper<UserDetails> result = await InvokeHubRawAsync<ResponseWrapper<UserDetails>>("Login", i_email, i_password);
                await DoLogin(result);
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "Login()");
                return ResponseWrapper<UserDetails>.Fail(ex.HResult, ex.Message);
            }
        }

        public async Task<ResponseWrapper<bool>> AcceptTermsAsync(int version)
        {
            try
            {
                await Initialize();
                return await InvokeHubRawAsync<ResponseWrapper<bool>>("AcceptTerms", _loginGuid, version);
            }
            catch (Exception ex)
            {
                await LogError(ex, "AcceptTermsAsync()");
                return ResponseWrapper<bool>.Fail(ex.HResult, ex.Message);
            }
        }

        public async Task<bool> IsLoggedIn()
        {
            try
            {
                _loginGuid = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "loginGuid");
                return !string.IsNullOrWhiteSpace(_loginGuid);
            }
            catch (Exception ex)
            {
                await LogError(ex);
                return false;
            }
        }

        public async Task<bool> GuaranteeConfirm(string verificationCode)
        {
            try
            {
                await Initialize();
                ResponseWrapper<bool> result = await InvokeHubRawAsync<ResponseWrapper<bool>>("GuaranteeConfirm", verificationCode);
                if (!result.IsSuccess) return false;
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "GuaranteeConfirm()");
                return false;
            }
        }
        public async Task<int> GuaranteeUser(string email, byte[] imageData)
        {
            await Initialize(); // Make sure the connection is initialized

            try
            {
                // Call the hub method GuaranteeUser
                ResponseWrapper<object> result = await InvokeHubRawAsync<ResponseWrapper<object>>("GuaranteeUser", _loginGuid, email, imageData);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.ErrorCode;
            }
            catch (Exception ex)
            {
                await LogError(ex, "Error in GuaranteeUser");
                return -1; // Return a default error code
            }
        }


        public async Task<(int, Guid)> UploadPhotoAsync(IBrowserFile file)
        {
            try
            {
                await Initialize();

                const long maxFileSize = 10485760; // 10 MB

                if (file.Size > maxFileSize)
                    return (-1, Guid.Empty);

                var imageData = new byte[file.Size];
                await file.OpenReadStream(maxFileSize).ReadExactlyAsync(imageData);

                // WASM-friendly image processing: use browser Canvas instead of ImageSharp.
                // Keep same semantics as Util.ProcessPhoto: aspectRatio=2, maxPixels=1,000,000, JPEG output.
                try
                {
                    const double aspectRatio = 2;
                    const int maxPixels = 1000000;
                    const double jpegQuality = 0.9;

                    string inputBase64 = Convert.ToBase64String(imageData);

                    string processedBase64 = await _jsRuntime.InvokeAsync<string>(
                        "window.companioNationImage.processPhotoBase64",
                        inputBase64,
                        aspectRatio,
                        maxPixels,
                        jpegQuality);

                    if (string.IsNullOrWhiteSpace(processedBase64))
                        return (-2, Guid.Empty);

                    imageData = Convert.FromBase64String(processedBase64);
                    if (imageData.Length == 0)
                        return (-2, Guid.Empty);
                }
                catch (Exception ex)
                {
                    await LogError(ex, "Client-side photo processing failed");
                    return (-2, Guid.Empty);
                }

                // Call the SignalR hub method to upload the photo.
                // Uses InvokeHubRawAsync to handle connection drops during JS processing.
                ResponseWrapper<Guid> result = await InvokeHubRawAsync<ResponseWrapper<Guid>>("UploadPhoto", _loginGuid, imageData);
                if (!result.IsSuccess)
                {
                    if (result.ErrorCode == 100000)
                    {
                        await RequestLogin();
                        return (-10, Guid.Empty);
                    }
                    else if (result.ErrorCode == ErrorCodes.FaceNotDetected)
                        return (-3, Guid.Empty); // No face detected
                    else
                        return (-4, Guid.Empty);
                }
                return (0, result.Data);
            }
            catch (Exception ex)
            {
                await LogError(ex, "UploadPhotoAsync()");
                return (ex.HResult, Guid.Empty);
            }
        }
        /// <summary>Sends a guarantee invitation to an email; returns the server ErrorCode (0 on success, -1 on exception).</summary>
        public async Task<int> GuaranteeUser(string email)
        {
            await Initialize(); // Make sure the connection is initialized

            try
            {
                ResponseWrapper<object> result = await InvokeHubRawAsync<ResponseWrapper<object>>("GuaranteeEmail", _loginGuid, email);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.ErrorCode;
            }
            catch (Exception ex)
            {
                await LogError(ex, "Error in GuaranteeUser");
                return -1; // Return a default error code
            }
        }

        /// <summary>Adds a user to the current user's ignore list; false on failure.</summary>
        public async Task<bool> AddIgnore(int userId)
        {
            ResponseWrapper<bool> result = await InvokeHubAsync<bool>("AddIgnore", _loginGuid, userId);
            return result.IsSuccess && result.Data;
        }

        /// <summary>Removes a user from the current user's ignore list; false on failure.</summary>
        public async Task<bool> RemoveIgnore(int userId)
        {
            ResponseWrapper<bool> result = await InvokeHubAsync<bool>("RemoveIgnore", _loginGuid, userId);
            return result.IsSuccess && result.Data;
        }

        /// <summary>Reports a user for objectionable content.</summary>
        public async Task<ReportResult?> ReportUserAsync(ReportRequest request)
        {
            await Initialize();

            try
            {
                var result = await InvokeHubRawAsync<ResponseWrapper<ReportResult>>("ReportUser", _loginGuid, request);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                    return null;
                }
                if (!result.IsSuccess)
                {
                    await LogError(new Exception(result.Message), $"ReportUserAsync() ErrorCode={result.ErrorCode}");
                    return null;
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "ReportUserAsync()");
                return null;
            }
        }

        /// <summary>Gets all pending reports (admin only).</summary>
        public async Task<List<PendingReport>> GetPendingReportsAsync()
        {
            await Initialize();

            try
            {
                var result = await InvokeHubRawAsync<ResponseWrapper<List<PendingReport>>>("GetPendingReports", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                    return new List<PendingReport>();
                }
                return result.Data ?? new List<PendingReport>();
            }
            catch (Exception ex)
            {
                await LogError(ex, "GetPendingReportsAsync()");
                return new List<PendingReport>();
            }
        }

        /// <summary>Resolves a report (admin only).</summary>
        public async Task<bool> ResolveReportAsync(int reportId, int status)
        {
            await Initialize();

            try
            {
                var result = await InvokeHubRawAsync<ResponseWrapper<bool>>("ResolveReport", _loginGuid, reportId, status);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                    return false;
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "ResolveReportAsync()");
                return false;
            }
        }

        /// <summary>Sets a user's mute status: muted users cannot send messages (admin only).</summary>
        public async Task<bool> SetMuteStatusAsync(int targetUserId, bool isMuted)
        {
            await Initialize();

            try
            {
                var result = await InvokeHubRawAsync<ResponseWrapper<bool>>("SetMuteStatus", _loginGuid, targetUserId, isMuted);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                    return false;
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "SetMuteStatusAsync()");
                return false;
            }
        }

        public async Task<UserConversation> StartUserConversationAsync(int userId)
        {
            await Initialize(); // Ensure the connection to SignalR Hub is established

            try
            {
                // Call the SignalR Hub method to get user details
                ResponseWrapper<UserConversation> result = await InvokeHubRawAsync<ResponseWrapper<UserConversation>>("StartUserConversation", _loginGuid, userId);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                    return null;
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "GetUserConversationAsync()");
                return null; // Return null or handle error appropriately
            }
        }

        /// <summary>Returns the users this account has guaranteed; empty list on failure.</summary>
        public async Task<List<GuaranteedUser>> GetGuaranteedUsersAsync()
        {
            ResponseWrapper<List<GuaranteedUser>> result = await InvokeHubAsync<List<GuaranteedUser>>("GetGuaranteedUsers", _loginGuid);
            return result.IsSuccess ? result.Data : new List<GuaranteedUser>();
        }


        /// <summary>Validates an email verification code; false if empty, invalid, or on error.</summary>
        public async Task<bool> CheckVerificationCode(string i_verificationCode)
        {
            if (string.IsNullOrEmpty(i_verificationCode)) return false;

            try
            {
                await Initialize();
                ResponseWrapper<object> result = await InvokeHubRawAsync<ResponseWrapper<object>>("CheckVerificationCode", i_verificationCode);
                if (!result.IsSuccess && result.ErrorCode != ErrorCodes.InvalidVerificationCode)
                {
                    await LogError(result);
                }
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                await LogError(ex, "CheckVerificationCode()");
                return false;
            }
        }
        /// <summary>Resets the password using a verification code; true on success, false on failure.</summary>
        public async Task<bool> ResetPassword(string i_verificationCode, string i_newPassword)
        {
            try
            {
                await Initialize();
                ResponseWrapper<object> result = await InvokeHubRawAsync<ResponseWrapper<object>>("ResetPassword", i_verificationCode, i_newPassword);
                if (!result.IsSuccess && result.ErrorCode != ErrorCodes.InvalidVerificationCode)
                {
                    await LogError(result);
                }
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                await LogError(ex, "ResetPassword()");
                return false;
            }
        }


        public async Task<string> GetLoginGuid()
        {
            try
            {
                await Initialize();
                return _loginGuid;
            }
            catch (Exception ex)
            {
                await LogError(ex);
                return "ERROR";
            }
        }


        /// <summary>Returns the current user's uploaded images; empty list on failure.</summary>
        public async Task<List<UserImage>> GetUserImagesAsync()
        {
            ResponseWrapper<List<UserImage>> result = await InvokeHubAsync<List<UserImage>>("GetUserImages", _loginGuid);
            return result.Data ?? [];
        }


        /// <summary>Returns messages the current user has ignored; empty list on failure.</summary>
        public async Task<List<UserMessage>> GetIgnoredMessagesAsync()
        {
            ResponseWrapper<List<UserMessage>> result = await InvokeHubAsync<List<UserMessage>>("GetIgnoredMessages", _loginGuid);
            return result.Data ?? new List<UserMessage>();
        }


        /// <summary>Searches for companions matching the given gender, city, and age filters; null on failure.</summary>
        public async Task<List<Companion>> FindCompanionsAsync(
            bool cisMale,
            bool cisFemale,
            bool other,
            bool transMale,
            bool transFemale,
            List<int> cities,
            int? ageFrom,
            int? ageTo,
            bool showIgnoredUsers)
        {
            try
            {
                // Call SignalR method (InvokeHubRawAsync retries connection drops)
                var result = await InvokeHubRawAsync<ResponseWrapper<List<Companion>>>(
                    "FindCompanions",
                    _loginGuid,
                    cisMale,
                    cisFemale,
                    other,
                    transMale,
                    transFemale,
                    cities,
                    ageFrom ?? 18,
                    ageTo ?? 99,
                    showIgnoredUsers
                );

                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "FindCompanionsAsync");
                return null;
            }
        }

        /// <summary>Requests a password-reset email for an address; returns true regardless of account existence to avoid information leakage.</summary>
        public async Task<bool> RequestPasswordReset(string i_email)
        {
            try
            {
                await Initialize();
                // Call the hub method to request a password-reset email
                await InvokeHubRawAsync<object>("RequestPasswordReset", i_email);
                return true; // Always return true regardless of the internal success to avoid information leakage
            }
            catch (Exception ex)
            {
                await LogError(ex, "RequestPasswordReset()");
                return false; // Return false if an error occurs, without exposing specific details
            }
        }

        /// <summary>Resends the signup email-verification link to the current account.</summary>
        public async Task<bool> ResendVerificationEmail()
        {
            try
            {
                await Initialize();
                ResponseWrapper<bool> result = await InvokeHubRawAsync<ResponseWrapper<bool>>("ResendVerificationEmail", _loginGuid);
                return result.IsSuccess && result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "ResendVerificationEmail()");
                return false;
            }
        }
        /// <summary>Returns the current user's conversation list; null on failure or transient timeout.</summary>
        public async Task<List<UserConversation>> GetUserConversationsAsync()
        {
            ResponseWrapper<List<UserConversation>> result = await InvokeHubAsync<List<UserConversation>>("GetUserConversations", _loginGuid);
            return result.Data;
        }

        /// <summary>Returns the message thread with a specific user; null on failure or transient timeout.</summary>
        public async Task<List<UserMessage>> GetMessagesWithUserAsync(int userId)
        {
            ResponseWrapper<List<UserMessage>> result = await InvokeHubAsync<List<UserMessage>>("GetMessagesWithUser", _loginGuid, userId);
            return result.Data;
        }

        public async Task<int> SendMessageAsync(int userId, string messageText)
        {
            try
            {
                await Initialize();
                ResponseWrapper<int> result = await InvokeHubRawAsync<ResponseWrapper<int>>("SendMessage", _loginGuid, userId, messageText);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (TimeoutException ex)
            {
                Console.WriteLine($"Transient timeout in SendMessageAsync: {ex.Message}");
                return 0;
            }
            catch (Exception ex)
            {
                await LogError(ex, "SendMessageAsync()");
                return 0;
            }
        }

        public async Task<bool> RemoveGuaranteeAsync(int imageId)
        {
            await Initialize(); // Ensure the SignalR connection is initialized

            try
            {
                // Call the hub method to remove the guarantee using the ImageID
                var response = await InvokeHubRawAsync<ResponseWrapper<bool>>("RemoveGuarantee", _loginGuid, imageId);

                // Check if the response indicates success
                if (response.IsSuccess)
                {
                    return true;
                }
                else
                {
                    // Log error details if the operation fails
                    await LogError($"Failed to remove guarantee. Error {response.ErrorCode}: {response.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                await LogError(ex, "Error in RemoveGuaranteeAsync()");
                return false; // Return false if an error occurs
            }
        }

        // ──── LINK Methods ────

        public async Task<string> GetLinkPayloadAsync()
        {
            try
            {
                await Initialize();
                ResponseWrapper<string> result = await InvokeHubRawAsync<ResponseWrapper<string>>("GetLinkPayload", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
                    await RequestLogin();
                    return null;
                }
                return result.IsSuccess ? result.Data : null;
            }
            catch (TimeoutException ex)
            {
                // Transient: the connection dropped while the invoke was in flight (common
                // on mobile when the app is backgrounded). The QR auto-refresh will retry.
                Console.WriteLine($"Transient timeout in GetLinkPayloadAsync: {ex.Message}");
                return null;
            }
            catch (InvalidOperationException ex)
            {
                // Transient: the connection went inactive between Initialize() and the
                // invoke (it closed and started reconnecting). Not a real error.
                Console.WriteLine($"Transient connection state in GetLinkPayloadAsync: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                await LogError(ex, "GetLinkPayloadAsync()");
                return null;
            }
        }

        /// <summary>
        /// Redeems a QR LINK code. Returns the linked user on success (ErrorCode=0),
        /// or the error code on failure with a null Data.
        /// </summary>
        public async Task<(LinkedUser? Data, int ErrorCode)> RedeemQrLinkAsync(string code)
        {
            try
            {
                await Initialize();
                ResponseWrapper<LinkedUser> result = await InvokeHubRawAsync<ResponseWrapper<LinkedUser>>("RedeemQrLink", _loginGuid, code);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
                    await RequestLogin();
                }
                return (result.Data, result.ErrorCode);
            }
            catch (Exception ex)
            {
                await LogError(ex, "RedeemQrLinkAsync()");
                return (null, ErrorCodes.UnknownError);
            }
        }

        public async Task<int> LinkEmailAsync(string email)
        {
            try
            {
                await Initialize();
                ResponseWrapper<object> result = await InvokeHubRawAsync<ResponseWrapper<object>>("LinkEmail", _loginGuid, email);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
                    await RequestLogin();
                }
                return result.ErrorCode;
            }
            catch (Exception ex)
            {
                await LogError(ex, "LinkEmailAsync()");
                return -1;
            }
        }

        /// <summary>Confirms an emailed LINK invitation and logs the recipient in.</summary>
        public async Task<ResponseWrapper<UserDetails>> ConfirmEmailLinkAsync(string verificationCode)
        {
            try
            {
                await Initialize();
                ResponseWrapper<UserDetails> result = await InvokeHubRawAsync<ResponseWrapper<UserDetails>>("ConfirmEmailLink", verificationCode);
                if (result.IsSuccess)
                {
                    await DoLogin(result);
                }
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "ConfirmEmailLinkAsync()");
                return ResponseWrapper<UserDetails>.Fail(ex.HResult, ex.Message);
            }
        }

        /// <summary>Rejects an emailed LINK invitation. No login is required.</summary>
        public async Task<ResponseWrapper<string>> RejectEmailLinkAsync(string verificationCode)
        {
            try
            {
                await Initialize();
                return await InvokeHubRawAsync<ResponseWrapper<string>>("RejectEmailLink", verificationCode);
            }
            catch (Exception ex)
            {
                await LogError(ex, "RejectEmailLinkAsync()");
                return ResponseWrapper<string>.Fail(ex.HResult, ex.Message);
            }
        }

        public async Task<List<LinkedUser>> GetLinkedUsersAsync()
        {
            try
            {
                await Initialize();
                ResponseWrapper<List<LinkedUser>> result = await InvokeHubRawAsync<ResponseWrapper<List<LinkedUser>>>("GetLinkedUsers", _loginGuid);
                if (!result.IsSuccess)
                {
                    if (result.ErrorCode == ErrorCodes.InvalidCredentials)
                    {
                        await RequestLogin();
                        return [];
                    }
                    await LogError($"Failed to load linked users (error {result.ErrorCode}).");
                    return [];
                }
                return result.Data ?? [];
            }
            catch (TimeoutException ex)
            {
                Console.WriteLine($"Transient timeout in GetLinkedUsersAsync: {ex.Message}");
                return [];
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Transient connection state in GetLinkedUsersAsync: {ex.Message}");
                return [];
            }
            catch (Exception ex)
            {
                await LogError(ex, "GetLinkedUsersAsync()");
                return [];
            }
        }

        public async Task<int> UploadLinkPhotoAsync(int connectionId, byte[] imageData)
        {
            try
            {
                await Initialize();
                ResponseWrapper<object> result = await InvokeHubRawAsync<ResponseWrapper<object>>("UploadLinkPhoto", _loginGuid, connectionId, imageData);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
                    await RequestLogin();
                }
                return result.ErrorCode;
            }
            catch (Exception ex)
            {
                await LogError(ex, "UploadLinkPhotoAsync()");
                return -1;
            }
        }

        public async Task<bool> DeleteUserPhotoAsync(int imageId)
        {
            try
            {
                await Initialize();
                ResponseWrapper<object> result = await InvokeHubRawAsync<ResponseWrapper<object>>("DeleteUserPhoto", _loginGuid, imageId);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
                    await RequestLogin();
                    return false;
                }
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                await LogError(ex, "DeleteUserPhotoAsync()");
                return false;
            }
        }

        public async Task<bool> SetLinkPhotoVisibilityAsync(int imageId, bool visible)
        {
            try
            {
                await Initialize();
                ResponseWrapper<object> result = await InvokeHubRawAsync<ResponseWrapper<object>>("SetLinkPhotoVisibility", _loginGuid, imageId, visible);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
                    await RequestLogin();
                    return false;
                }
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                await LogError(ex, "SetLinkPhotoVisibilityAsync()");
                return false;
            }
        }

        /// <summary>
        /// Confirms a LINK photo (subject confirms "yes, that's me").
        /// </summary>
        public async Task<bool> ConfirmLinkPhotoAsync(int imageId)
        {
            try
            {
                await Initialize();
                ResponseWrapper<object> result = await InvokeHubRawAsync<ResponseWrapper<object>>("ConfirmLinkPhoto", _loginGuid, imageId);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
                    await RequestLogin();
                    return false;
                }
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                await LogError(ex, "ConfirmLinkPhotoAsync()");
                return false;
            }
        }

        /// <summary>
        /// Rejects a LINK photo (subject says "that's not me"). Photo is deleted
        /// and uploader loses 1 karma.
        /// </summary>
        public async Task<bool> RejectLinkPhotoAsync(int imageId)
        {
            try
            {
                await Initialize();
                ResponseWrapper<object> result = await InvokeHubRawAsync<ResponseWrapper<object>>("RejectLinkPhoto", _loginGuid, imageId);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
                    await RequestLogin();
                    return false;
                }
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                await LogError(ex, "RejectLinkPhotoAsync()");
                return false;
            }
        }

        public async Task<List<KarmaDesync>> RecalculateKarmaAsync()
        {
            try
            {
                await Initialize();
                ResponseWrapper<List<KarmaDesync>> result = await InvokeHubRawAsync<ResponseWrapper<List<KarmaDesync>>>("RecalculateKarma", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
                    await RequestLogin();
                }
                return result.Data ?? [];
            }
            catch (Exception ex)
            {
                await LogError(ex, "RecalculateKarmaAsync()");
                return [];
            }
        }

        public async Task<GuarantorMigrationResult?> MigrateGuarantorDataAsync()
        {
            try
            {
                await Initialize();
                ResponseWrapper<GuarantorMigrationResult> result = await InvokeHubRawAsync<ResponseWrapper<GuarantorMigrationResult>>("MigrateGuarantorData", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "MigrateGuarantorDataAsync()");
                return null;
            }
        }


        public async Task<bool> UpdateUserDetailsAsync(UserDetails userDetails)
        {
            try
            {
                await Initialize(); // Ensure the connection is initialized

                // Call the SignalR hub method to update user details by passing the
                // UserDetails object. InvokeHubRawAsync handles the
                // connection-dropped-during-call retry — a direct
                // _hubConnection.InvokeAsync can crash on an inactive connection.
                ResponseWrapper<bool> result = await InvokeHubRawAsync<ResponseWrapper<bool>>(
                    "UpdateUserDetails",
                    _loginGuid,
                    userDetails
                );

                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }

                // Only cache the new details when the server actually accepted them.
                if (result.IsSuccess)
                {
                    _currentUser = userDetails;
                }

                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "Error in UpdateUserDetailsAsync()");
                return false; // Return false if an error occurs
            }
        }

        /// <summary>Stages an email address change; returns the verification code or an error response.</summary>
        public async Task<ResponseWrapper<string>> RequestEmailChangeAsync(string newEmail)
        {
            try
            {
                await Initialize();
                return await InvokeHubRawAsync<ResponseWrapper<string>>("RequestEmailChange", _loginGuid, newEmail);
            }
            catch (Exception ex)
            {
                await LogError(ex, "RequestEmailChangeAsync()");
                return ResponseWrapper<string>.Fail(ex.HResult, ex.Message);
            }
        }

        /// <summary>Confirms a staged email address change using the code sent to the new address.</summary>
        public async Task<ResponseWrapper<bool>> ConfirmEmailChangeAsync(string verificationCode)
        {
            try
            {
                await Initialize();
                return await InvokeHubRawAsync<ResponseWrapper<bool>>("ConfirmEmailChange", _loginGuid, verificationCode);
            }
            catch (Exception ex)
            {
                await LogError(ex, "ConfirmEmailChangeAsync()");
                return ResponseWrapper<bool>.Fail(ex.HResult, ex.Message);
            }
        }

        /// <summary>
        /// Soft-deletes the current user's profile and clears the local session.
        /// </summary>
        public async Task<bool> DeleteProfileAsync()
        {
            try
            {
                ResponseWrapper<bool> result = await InvokeHubRawAsync<ResponseWrapper<bool>>(
                    "DeleteProfile",
                    _loginGuid
                );

                if (result.IsSuccess && result.Data)
                {
                    // The server has scrubbed the account and cleared its push token.
                    // Clear the local session and unregister the Web Push subscription
                    // so notifications can never arrive on this device after deletion.
                    _currentUser = null;
                    _loginGuid = null;
                    await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "loginGuid");
                    await _jsRuntime.InvokeVoidAsync("window.unregisterPush");
                    return true;
                }

                if (result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }

                return false;
            }
            catch (Exception ex)
            {
                await LogError(ex, "Error in DeleteProfileAsync()");
                return false;
            }
        }



        // Method to update the visibility of an image
        public async Task UpdateImageVisibility(int imageId, bool isVisible)
        {
            try
            {
                // Call the SignalR hub method to update image visibility
                var result = await InvokeHubRawAsync<ResponseWrapper<bool>>(
                    "UpdateImageVisibility",
                    _loginGuid,
                    imageId,
                    isVisible);

                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return;
            }
            catch (Exception ex)
            {
                await LogError(ex, "Error in UpdateImageVisibility()");
            }
        }


        public async Task<bool> UpdateReviewVisibility(int imageId, bool isPublic)
        {
            try
            {
                // Call the SignalR hub method to update the visibility
                var result = await InvokeHubRawAsync<ResponseWrapper<bool>>(
                    "UpdateReviewVisibility",
                    _currentUser.LoginToken.ToString(),
                    imageId,
                    isPublic
                );

                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin(); // Prompt re-login if the token is invalid
                }

                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                await LogError(ex, "Error in UpdateReviewVisibility()");
                return false;
            }
        }



        public async Task<bool> UpdateImageReview(int imageId, int rating, string review)
        {
            try
            {
                // Call the SignalR hub method to update the rating and review
                var result = await InvokeHubRawAsync<ResponseWrapper<bool>>(
                    "UpdateImageReview",
                    _loginGuid,
                    imageId,
                    rating,
                    review
                );

                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin(); // Prompt re-login if the token is invalid
                }

                return result.Data; // Return the response from the SignalR hub
            }
            catch (Exception ex)
            {
                await LogError(ex, "Error in UpdateImageReview()"); // Log any errors
                return false;
            }
        }

        /// <summary>Saves the caller's rating/review of a linked user; false on failure.</summary>
        public async Task<bool> SetConnectionReview(int connectionId, int rating, string review)
        {
            ResponseWrapper<bool> result = await InvokeHubAsync<bool>("SetConnectionReview", _loginGuid, connectionId, rating, review);
            return result.IsSuccess && result.Data;
        }

        /// <summary>Toggles whether a review about the current user is publicly visible; false on failure.</summary>
        public async Task<bool> SetConnectionReviewVisibility(int connectionId, bool isVisible)
        {
            ResponseWrapper<bool> result = await InvokeHubAsync<bool>("SetConnectionReviewVisibility", _loginGuid, connectionId, isVisible);
            return result.IsSuccess && result.Data;
        }



        public async Task<string> TriggerMaintenanceManually()
        {
            try
            {
                // Call the hub method to trigger maintenance
                ResponseWrapper<string> result = await InvokeHubRawAsync<ResponseWrapper<string>>("TriggerMaintenanceManually", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex);
                return "Unknown Error Occurred: " + ex.Message + ex.StackTrace;
            }
        }


        public async Task<string> RunTestSuite()
        {
            try
            {
                ResponseWrapper<string> result = await InvokeHubRawAsync<ResponseWrapper<string>>("RunTestSuite", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex);
                return "ERROR: " + ex.Message + ex.StackTrace;
            }
        }
        public async Task<bool> SendAccountCreationEmail(string email)
        {
            // Implement the logic to send the email with a link to complete the profile creation
            // Return true if the email was sent successfully, otherwise return false

            return true;
        }

        public async Task<List<Country>> GetCountries(string continent)
        {
            /*
            Continent codes :
            AF : Africa			geonameId=6255146
            AS : Asia			geonameId=6255147
            EU : Europe			geonameId=6255148
            NA : North America		geonameId=6255149
            OC : Oceania			geonameId=6255151
            SA : South America		geonameId=6255150
            AN : Antarctica			geonameId=6255152
            */

            ResponseWrapper<List<Country>> result = await InvokeHubAsync<List<Country>>("GetCountries", continent);
            return result.Data ?? new List<Country>();
        }
        public async Task<List<City>> GetNearbyCities()
        {
            ResponseWrapper<List<City>> result = await InvokeHubAsync<List<City>>("GetNearbyCities", _loginGuid);
            return result.Data ?? new List<City>();
        }
        public async Task<List<City>> GetCities(string country, string searchTerm)
        {
            ResponseWrapper<List<City>> result = await InvokeHubAsync<List<City>>("GetCities", country, searchTerm);
            return result.Data ?? new List<City>();
        }
        public async Task<City> GetCity(int geonameid)
        {
            ResponseWrapper<City> result = await InvokeHubAsync<City>("GetCity", _loginGuid, geonameid);
            return result.Data;
        }
        public async Task<List<City>> GetNearestCities(double latitude, double longitude)
        {
            ResponseWrapper<List<City>> result = await InvokeHubAsync<List<City>>("GetNearestCities", _loginGuid, latitude, longitude);
            return result.Data ?? new List<City>();
        }
        public async Task<CheckEmailResult> CheckEmailExists(string email)
        {
            try
            {
                await Initialize();
                ResponseWrapper<CheckEmailResult> result = await InvokeHubRawAsync<ResponseWrapper<CheckEmailResult>>("CheckEmailExists", email);
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "CheckEmailExists()");
                CheckEmailResult result = new CheckEmailResult();
                result.emailExists = false;
                result.oauthRequired = false;
                return result;
            }
        }

        public async Task<bool> CreateNewUser(string email, string password)
        {
            try
            {
                await Initialize();
                ResponseWrapper<bool> result = await InvokeHubRawAsync<ResponseWrapper<bool>>("CreateNewUser", email, password);
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "CreateNewUser()");
                return false;
            }
        }


        public async Task SendFeedback(string feedbackText)
        {
            try
            {
                await Initialize(); // Ensure the connection is initialized
                string debugInfo = await BuildFeedbackDebugInfo();
                await InvokeHubRawAsync<object>("ReceiveFeedback", _loginGuid, feedbackText, debugInfo);
            }
            catch (Exception ex)
            {
                await LogError(ex, "SendFeedback");
            }
        }

        /// <summary>
        /// Builds a diagnostics block (browser/platform/device/version/route state)
        /// that is appended to user feedback emails so issues can be triaged from the
        /// inbox without needing to ask the user for their environment.
        /// </summary>
        private async Task<string> BuildFeedbackDebugInfo()
        {
            await EnsureClientInfoAsync();

            var sb = new StringBuilder();
            sb.AppendLine("--- CLIENT DEBUG INFO ---");
            if (!string.IsNullOrWhiteSpace(_clientInfo))
            {
                sb.AppendLine(_clientInfo);
            }
            sb.AppendLine($"ClientVersion: {Util.GetCurrentVersion()}");
            sb.AppendLine($"ServerVersion: {_currentVersion ?? "unknown"}");
            sb.AppendLine($"NavigationUri: {_navigationManager.Uri}");
            sb.AppendLine($"LoginGuidPresent: {!string.IsNullOrWhiteSpace(_loginGuid)}");
            sb.AppendLine($"HasUser: {_currentUser != null}");
            sb.AppendLine($"UserId: {_currentUser?.UserId.ToString() ?? "not-logged-in"}");
            sb.AppendLine($"Email: {(string.IsNullOrWhiteSpace(_currentUser?.Email) ? "not-logged-in" : _currentUser.Email)}");
            sb.AppendLine($"HubState: {_hubConnection?.State}");
            sb.AppendLine($"HubConnectionId: {_hubConnection?.ConnectionId ?? "null"}");
            sb.AppendLine($"TimestampLocal: {DateTime.Now:O}");
            sb.AppendLine($"TimestampUtc: {DateTime.UtcNow:O} (Vancouver: {Util.FormatVancouverTime(DateTime.UtcNow)})");
            return sb.ToString();
        }

        public async Task<ResponseWrapper<UserDetails>> LoginWithGoogle(string code, string code_verifier, string redirect_uri)
        {
            try
            {
                await Initialize(); // Ensure the SignalR connection is initialized

                // Call the SignalR hub method to log in with Google
                ResponseWrapper<UserDetails> result = await InvokeHubRawAsync<ResponseWrapper<UserDetails>>("LoginWithGoogle", code, code_verifier, redirect_uri);
                await DoLogin(result);
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "LoginWithGoogle()");
                return ResponseWrapper<UserDetails>.Fail(ex.HResult, ex.Message);
            }
        }

        public async Task<ResponseWrapper<UserDetails>> LoginWithApple(string code, string redirect_uri, string? firstName, string? lastName)
        {
            try
            {
                await Initialize();

                ResponseWrapper<UserDetails> result = await InvokeHubRawAsync<ResponseWrapper<UserDetails>>(
                    "LoginWithApple",
                    code, redirect_uri, firstName, lastName);
                await DoLogin(result);
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "LoginWithApple()");
                return ResponseWrapper<UserDetails>.Fail(ex.HResult, ex.Message);
            }
        }

        public async Task<ResponseWrapper<UserDetails>> LoginWithFacebook(string code, string code_verifier, string redirect_uri)
        {
            try
            {
                await Initialize();

                ResponseWrapper<UserDetails> result = await InvokeHubRawAsync<ResponseWrapper<UserDetails>>("LoginWithFacebook", code, code_verifier, redirect_uri);
                await DoLogin(result);
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "LoginWithFacebook()");
                return ResponseWrapper<UserDetails>.Fail(ex.HResult, ex.Message);
            }
        }

        public async Task<ResponseWrapper<UserDetails>> LoginWithTwitter(string code, string code_verifier, string redirect_uri)
        {
            try
            {
                await Initialize();

                ResponseWrapper<UserDetails> result = await InvokeHubRawAsync<ResponseWrapper<UserDetails>>("LoginWithTwitter", code, code_verifier, redirect_uri);
                await DoLogin(result);
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "LoginWithTwitter()");
                return ResponseWrapper<UserDetails>.Fail(ex.HResult, ex.Message);
            }
        }

        public async Task<ResponseWrapper<UserDetails>> LoginWithMicrosoft(string code, string code_verifier, string redirect_uri)
        {
            try
            {
                await Initialize();

                ResponseWrapper<UserDetails> result = await InvokeHubRawAsync<ResponseWrapper<UserDetails>>("LoginWithMicrosoft", code, code_verifier, redirect_uri);
                await DoLogin(result);
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "LoginWithMicrosoft()");
                return ResponseWrapper<UserDetails>.Fail(ex.HResult, ex.Message);
            }
        }

        public async Task<ResponseWrapper<OAuthConfig>> GetOAuthConfig()
        {
            // InvokeHubAsync retries connection drops and treats timeouts/network
            // failures as soft failures, so transient reconnects don't spam the log.
            return await InvokeHubAsync<OAuthConfig>("GetOAuthConfig");
        }


        // =============================================
        // Admin Profile Moderation Methods
        // =============================================

        /// <summary>
        /// Retrieves a paginated list of profiles for admin triage.
        /// Sorted by unresolved report count (most first). Supports optional search by name, email, or user ID.
        /// </summary>
        public async Task<ResponseWrapper<List<UserDetails>>> AdminGetFlaggedProfilesAsync(int offset, int count, string? searchTerm = null)
        {
            try
            {
                await Initialize();
                var result = await InvokeHubRawAsync<ResponseWrapper<List<UserDetails>>>("AdminGetFlaggedProfiles", _loginGuid, offset, count, searchTerm);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminGetFlaggedProfilesAsync()");
                return ResponseWrapper<List<UserDetails>>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Retrieves full profile details and photos for admin audit.
        /// </summary>
        public async Task<ResponseWrapper<UserDetails>> AdminGetProfileForAuditAsync(int userId)
        {
            try
            {
                await Initialize();
                var result = await InvokeHubRawAsync<ResponseWrapper<UserDetails>>("AdminGetProfileForAudit", _loginGuid, userId);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminGetProfileForAuditAsync()");
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Admin updates a user's profile fields via the existing UpdateUserDetails flow.
        /// userDetails.UserId identifies the target user.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminUpdateProfileAsync(UserDetails userDetails)
        {
            try
            {
                await Initialize();
                var result = await InvokeHubRawAsync<ResponseWrapper<bool>>("AdminUpdateProfile", _loginGuid, userDetails);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminUpdateProfileAsync()");
                return ResponseWrapper<bool>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Admin updates account-level attributes (subscription expiry, admin status,
        /// verification, mute state, optional password) for a target user.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminUpdateUserAttributesAsync(AdminUserAttributes attributes)
        {
            try
            {
                await Initialize();
                var result = await InvokeHubRawAsync<ResponseWrapper<bool>>("AdminUpdateUserAttributes", _loginGuid, attributes);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminUpdateUserAttributesAsync()");
                return ResponseWrapper<bool>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Retrieves aggregated site-wide statistics for the admin dashboard.
        /// </summary>
        public async Task<ResponseWrapper<SiteStats>> AdminGetSiteStatsAsync()
        {
            try
            {
                var result = await InvokeHubRawAsync<ResponseWrapper<SiteStats>>("AdminGetSiteStats", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminGetSiteStatsAsync()");
                return ResponseWrapper<SiteStats>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Admin deletes a photo from a user's profile.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminDeletePhotoAsync(int userId, int imageId)
        {
            try
            {
                await Initialize();
                var result = await InvokeHubRawAsync<ResponseWrapper<bool>>("AdminDeletePhoto", _loginGuid, userId, imageId);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminDeletePhotoAsync()");
                return ResponseWrapper<bool>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Returns the event badges awarded to a user.
        /// </summary>
        public async Task<ResponseWrapper<List<EventBadge>>> GetUserBadgesAsync(int targetUserId)
        {
            try
            {
                await Initialize();
                var result = await InvokeHubRawAsync<ResponseWrapper<List<EventBadge>>>("GetUserBadges", _loginGuid, targetUserId);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "GetUserBadgesAsync()");
                return ResponseWrapper<List<EventBadge>>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Returns all event badge definitions for the admin badge editor.
        /// </summary>
        public async Task<ResponseWrapper<List<EventBadge>>> AdminListEventBadgesAsync()
        {
            try
            {
                await Initialize();
                var result = await InvokeHubRawAsync<ResponseWrapper<List<EventBadge>>>("AdminListEventBadges", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminListEventBadgesAsync()");
                return ResponseWrapper<List<EventBadge>>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Admin awards an event badge to a user.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminAwardEventBadgeAsync(int targetUserId, int badgeId)
        {
            return await AdminSetEventBadgeAsync(targetUserId, badgeId, award: true);
        }

        /// <summary>
        /// Admin revokes an event badge from a user.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminRevokeEventBadgeAsync(int targetUserId, int badgeId)
        {
            return await AdminSetEventBadgeAsync(targetUserId, badgeId, award: false);
        }

        private async Task<ResponseWrapper<bool>> AdminSetEventBadgeAsync(int targetUserId, int badgeId, bool award)
        {
            try
            {
                await Initialize();
                var method = award ? "AdminAwardEventBadge" : "AdminRevokeEventBadge";
                var result = await InvokeHubRawAsync<ResponseWrapper<bool>>(method, _loginGuid, targetUserId, badgeId);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, award ? "AdminAwardEventBadgeAsync()" : "AdminRevokeEventBadgeAsync()");
                return ResponseWrapper<bool>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Admin sends a broadcast notification or a targeted notification to one user.
        /// </summary>
        public async Task<ResponseWrapper<BroadcastResult>> AdminSendBroadcastNotificationAsync(string title, string body, string? url, string? targetEmail = null)
        {
            try
            {
                await Initialize();
                var result = await InvokeHubRawAsync<ResponseWrapper<BroadcastResult>>("AdminSendBroadcastNotification", _loginGuid, title, body, url, targetEmail);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminSendBroadcastNotificationAsync()");
                return ResponseWrapper<BroadcastResult>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Finds Azure blobs with no matching cn_images record (orphaned images).
        /// </summary>
        public async Task<ResponseWrapper<List<OrphanedImage>>> AdminFindOrphanedImagesAsync()
        {
            try
            {
                await Initialize();
                var result = await InvokeHubRawAsync<ResponseWrapper<List<OrphanedImage>>>("AdminFindOrphanedImages", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminFindOrphanedImagesAsync()");
                return ResponseWrapper<List<OrphanedImage>>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Deletes the confirmed list of orphaned blobs from Azure storage.
        /// </summary>
        public async Task<ResponseWrapper<int>> AdminDeleteOrphanedImagesAsync(List<Guid> imageGuids)
        {
            try
            {
                await Initialize();
                var result = await InvokeHubRawAsync<ResponseWrapper<int>>("AdminDeleteOrphanedImages", _loginGuid, imageGuids);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminDeleteOrphanedImagesAsync()");
                return ResponseWrapper<int>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Admin dismisses a profile from the triage queue.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminDismissProfileAsync(int userId)
        {
            try
            {
                var result = await InvokeHubRawAsync<ResponseWrapper<bool>>("AdminDismissProfile", _loginGuid, userId);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminDismissProfileAsync()");
                return ResponseWrapper<bool>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Admin soft-deletes a target user's account.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminDeleteProfileAsync(int userId)
        {
            try
            {
                await Initialize();
                var result = await InvokeHubRawAsync<ResponseWrapper<bool>>("AdminDeleteProfile", _loginGuid, userId);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminDeleteProfileAsync()");
                return ResponseWrapper<bool>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Admin checks a single photo for compliance without deleting it.
        /// </summary>
        public async Task<ResponseWrapper<string>> AdminCheckPhotoAsync(Guid imageGuid)
        {
            try
            {
                var result = await InvokeHubRawAsync<ResponseWrapper<string>>("AdminCheckPhoto", _loginGuid, imageGuid);
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    await RequestLogin();
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminCheckPhotoAsync()");
                return ResponseWrapper<string>.Fail(ErrorCodes.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// Streams progress of a bulk photo compliance scan. Calls onProgress with JSON status updates.
        /// Returns when the stream completes or is cancelled.
        /// </summary>
        public async Task AdminCheckAllPhotosAsync(Action<string> onProgress, CancellationToken cancellationToken)
        {
            try
            {
                await Initialize();
                await foreach (string update in _hubConnection.StreamAsync<string>(
                    "AdminCheckAllPhotos", _loginGuid, cancellationToken))
                {
                    onProgress(update);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when user cancels
            }
            catch (Exception ex)
            {
                await LogError(ex, "AdminCheckAllPhotosAsync()");
                onProgress($"{{\"error\":\"{ex.Message}\"}}");
            }
        }

    }
}
