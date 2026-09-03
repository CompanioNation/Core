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
                    ResponseWrapper<ConnectResult> result = await InvokeHubAsync<ConnectResult>("Connect", new ConnectRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
                    serverVersion = result.Version ?? string.Empty;

                    if (result.IsSuccess)
                    {
                        Util.InitializePhotoBaseUrl(result.Data.PhotosBaseUrl);
                        Console.WriteLine("Photos Base Url: " + result.Data.PhotosBaseUrl);
                        if (result.Data.CurrentUser != null )
                        {
                            _currentUser = result.Data.CurrentUser.Data;
                            if (result.Data.CurrentUser.ErrorCode == ErrorCodes.InvalidCredentials)
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
                    try { serverVersion = await InvokeHubRawAsync<string>("GetCurrentVersion", new GetCurrentVersionRequest { ClientVersion = Util.GetCurrentVersion() }); }
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
        /// <summary>
        /// Per-call behavior flags for the hub-invocation helpers. Callers OPT IN or OUT of
        /// a behavior explicitly instead of the helper branching on a method-name string.
        /// </summary>
        [Flags]
        private enum HubInvokeOptions
        {
            None = 0,

            /// <summary>Prompt the user to log in when the server returns <see cref="ErrorCodes.InvalidCredentials"/>.</summary>
            RequestLoginOnInvalidCredentials = 1 << 0,

            /// <summary>Fire the "update available" prompt when the server returns <see cref="ErrorCodes.ClientUpgradeRequired"/>.</summary>
            PromptUpdateOnUpgradeRequired = 1 << 1,

            /// <summary>Report unexpected failures (HubException/generic) to the server error log.</summary>
            LogFailures = 1 << 2,

            Default = RequestLoginOnInvalidCredentials | PromptUpdateOnUpgradeRequired | LogFailures,
        }

        /// <summary>
        /// THE single hub-invocation path for every method that returns a
        /// <see cref="ResponseWrapper{T}"/>. It owns connection setup, transient retry,
        /// and the first-class soft results configured by <paramref name="options"/>:
        /// <see cref="ErrorCodes.InvalidCredentials"/> (login prompt) and
        /// <see cref="ErrorCodes.ClientUpgradeRequired"/> (update prompt).
        /// Call sites must NOT re-check either code — those are handled here.
        /// </summary>
        private async Task<ResponseWrapper<T>> InvokeHubAsync<T>(string methodName, object? request, HubInvokeOptions options = HubInvokeOptions.Default)
        {
            if (_isPrerendering)
                return ResponseWrapper<T>.Fail(ErrorCodes.UnknownError, "Hub unavailable during SSR prerendering.");

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await Initialize();
                    ResponseWrapper<T> result = await _hubConnection.InvokeCoreAsync<ResponseWrapper<T>>(methodName, new object?[] { request }, CancellationToken.None);

                    if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                    {
                        // Session expired/invalid. The fail wrapper is still returned so the
                        // caller can fall back to its default value; whether to prompt login
                        // is caller-configurable (e.g. the Login method itself opts out).
                        if ((options & HubInvokeOptions.RequestLoginOnInvalidCredentials) != 0)
                        {
                            await RequestLogin();
                        }
                        return result;
                    }

                    if (!result.IsSuccess && result.ErrorCode == ErrorCodes.ClientUpgradeRequired)
                    {
                        // A soft contract result, NOT an error. Never log or buffer it.
                        if ((options & HubInvokeOptions.PromptUpdateOnUpgradeRequired) != 0)
                        {
                            OnUpdateAvailable?.Invoke();
                        }
                        return ResponseWrapper<T>.Fail(
                            ErrorCodes.ClientUpgradeRequired,
                            "A new version of CompanioNation is available. Please refresh to continue.");
                    }

                    return result;
                }
                catch (InvalidOperationException ex) when (attempt == 1)
                {
                    // The connection dropped between Initialize() and the invoke (common on
                    // mobile when the app is backgrounded, or during a long JS interop step).
                    // Auto-reconnect is already in progress — give it a beat, then retry.
                    Console.WriteLine($"Transient connection state in {methodName}; retrying: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(8));
                }
                catch (HttpRequestException ex) when (attempt <= 2)
                {
                    // Browser-level network failure (e.g., "TypeError: Failed to fetch").
                    // Transient — delay and retry a couple of times before soft-failing.
                    Console.WriteLine($"Transient network error in {methodName} (attempt {attempt}): {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }
                catch (WebSocketException ex) when (attempt <= 2)
                {
                    // WebSocket transport broken (e.g. connection dropped mid-message).
                    // Transient — delay and retry a couple of times before soft-failing.
                    Console.WriteLine($"Transient WebSocket error in {methodName} (attempt {attempt}): {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }
                catch (TimeoutException ex)
                {
                    // Server did not respond in time — almost always a transient network
                    // drop. Don't spam the server error log; surface a soft failure.
                    Console.WriteLine($"Transient timeout in {methodName}: {ex.Message}");
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

                    if ((options & HubInvokeOptions.LogFailures) != 0)
                    {
                        await LogError(ex, $"{methodName}()");
                    }
                    return ResponseWrapper<T>.Fail(ErrorCodes.UnknownError, ex.Message);
                }
                catch (Exception ex)
                {
                    if ((options & HubInvokeOptions.LogFailures) != 0)
                    {
                        await LogError(ex, $"{methodName}()");
                    }
                    return ResponseWrapper<T>.Fail(ErrorCodes.UnknownError, ex.Message);
                }
            }

            // Exhausted transient retries without a definitive server answer.
            return ResponseWrapper<T>.Fail(ErrorCodes.UnknownError, "Unable to reach the server.");
        }

        /// <summary>
        /// The single hub-invocation path for methods that return NO payload (currently
        /// <c>RequestPasswordReset</c> and <c>ReceiveFeedback</c>). Shares the same
        /// connect/retry/transient handling as <see cref="InvokeHubAsync{T}"/>, but has no
        /// ResponseWrapper to inspect, so failures are swallowed after logging.
        /// </summary>
        private async Task InvokeHubVoidAsync(string methodName, object? request, HubInvokeOptions options = HubInvokeOptions.Default)
        {
            if (_isPrerendering) return;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await Initialize();
                    await _hubConnection.InvokeCoreAsync(methodName, typeof(object), new object?[] { request }, CancellationToken.None);
                    return;
                }
                catch (InvalidOperationException ex) when (attempt == 1)
                {
                    Console.WriteLine($"Transient connection state in {methodName}; retrying: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(8));
                }
                catch (HttpRequestException ex) when (attempt <= 2)
                {
                    Console.WriteLine($"Transient network error in {methodName} (attempt {attempt}): {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }
                catch (WebSocketException ex) when (attempt <= 2)
                {
                    Console.WriteLine($"Transient WebSocket error in {methodName} (attempt {attempt}): {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }
                catch (TimeoutException ex)
                {
                    Console.WriteLine($"Transient timeout in {methodName}: {ex.Message}");
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

                    if ((options & HubInvokeOptions.LogFailures) != 0)
                    {
                        await LogError(ex, $"{methodName}()");
                    }
                    return;
                }
                catch (Exception ex)
                {
                    if ((options & HubInvokeOptions.LogFailures) != 0)
                    {
                        await LogError(ex, $"{methodName}()");
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Scalar-only hub invocation for the rare methods that do NOT return a
        /// <see cref="ResponseWrapper{T}"/> (e.g. <c>GetCurrentVersion</c>). Everything
        /// ResponseWrapper-based MUST use <see cref="InvokeHubAsync{T}"/> so login/upgrade
        /// handling stays centralized. Transient exceptions are retried; anything else
        /// propagates to the caller.
        /// </summary>
        private async Task<T> InvokeHubRawAsync<T>(string methodName, object? request)
        {
            if (_isPrerendering)
                throw new InvalidOperationException("Cannot invoke hub methods during SSR prerendering.");

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await Initialize();
                    return await _hubConnection.InvokeCoreAsync<T>(methodName, new object?[] { request }, CancellationToken.None);
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
                ResponseWrapper<bool> result = await InvokeHubAsync<bool>("UpdatePushToken", new UpdatePushTokenRequest { LoginToken = _loginGuid, PushToken = pushToken, ClientVersion = Util.GetCurrentVersion() });
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

                // Bound the backlog so a long offline stretch can't grow localStorage
                // without limit (and later produce an oversized single dump email).
                // Drop the oldest entries once the cap is reached.
                const int MaxLocalLogEntries = 25;
                if (logEntries.Count > MaxLocalLogEntries)
                {
                    logEntries.RemoveRange(0, logEntries.Count - MaxLocalLogEntries);
                }

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

                // Drop entries recorded by an OLDER client build. After a PWA update the
                // backlog almost always contains version-skew artifacts (the cached old
                // bundle failed hub calls whose signatures changed mid-deploy, then queued
                // the failures locally because its own LogError arity was rejected). Those
                // are NOT real errors, but every updating client would otherwise dump them
                // straight into the error email pipeline — a flood that scales with the
                // number of clients. Entries recorded by the CURRENT build (e.g. genuine
                // offline failures) are kept and flushed in a single call below.
                string currentVersion = Util.GetCurrentVersion();
                List<LogEntry> actionable = logEntries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.version) &&
                                    string.Equals(entry.version, currentVersion, StringComparison.Ordinal))
                    .ToList();

                int staleSkipped = logEntries.Count - actionable.Count;
                if (staleSkipped > 0)
                {
                    Console.WriteLine($"Local log dump: discarding {staleSkipped} stale entr{(staleSkipped == 1 ? "y" : "ies")} recorded by an older client version (version-skew artifacts, not actionable).");
                }

                if (actionable.Count == 0)
                {
                    // Nothing worth reporting — clear the stale backlog silently.
                    await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "errorLog");
                    return;
                }

                // Send the ENTIRE remaining backlog in a single LogError invocation. Sending one
                // call per entry made every reconnect burst of stored client errors fan
                // out into one server email per entry, each consuming a slot of the
                // shared email budget. Each entry keeps its recorded version/timestamp
                // so the single email still shows when and on what build each error
                // happened.
                int totalEntries = actionable.Count;
                var sb = new StringBuilder();
                sb.Append($"====== LOCAL LOG DUMP ({totalEntries} {(totalEntries == 1 ? "entry" : "entries")}) ======");

                // Stamp the current session identity on the dump itself so the recipient
                // can see WHICH account (if any) hit these errors even when the stored
                // entries were captured before the session was fully restored.
                sb.Append("\nUserId: ").Append(_currentUser?.UserId.ToString() ?? "not-logged-in");
                sb.Append("\nEmail: ").Append(string.IsNullOrWhiteSpace(_currentUser?.Email) ? "not-logged-in" : _currentUser.Email);

                foreach (LogEntry entry in actionable)
                {
                    sb.Append("\n\n----- ");
                    sb.Append(entry.timestamp.ToString("u"));
                    if (!string.IsNullOrWhiteSpace(entry.version))
                    {
                        sb.Append("  v").Append(entry.version);
                    }
                    sb.Append(" -----\n");
                    sb.Append(entry.message ?? string.Empty);
                }

                await _hubConnection.InvokeAsync("LogError", new LogErrorRequest { ClientVersion = Util.GetCurrentVersion(), Timestamp = DateTime.UtcNow, Message = sb.ToString(), Version = Util.GetCurrentVersion() });

                // Only clear the backlog after the single send succeeded. If the call
                // throws, the entries are left untouched so the whole dump retries on
                // the next successful reconnect — nothing is partially lost or duplicated.
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "errorLog");
            }
            catch (Exception ex)
            {
                // Keep the backlog for the next reconnect. Console-only: appending here
                // would duplicate on every failed attempt and grow the very dump we are
                // trying to keep bounded.
                Console.Error.WriteLine($"Local log dump failed (will retry on reconnect): {ex.Message} {ex.StackTrace}");
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
                // Chain rendering lives in exactly one place (CompanioNation.Shared) so the
                // browser client and the server error pipeline can never drift on format.
                ExceptionFormatter.AppendChain(sb, exception);
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
                await _hubConnection.InvokeAsync("LogError", new LogErrorRequest { ClientVersion = Util.GetCurrentVersion(), Timestamp = DateTime.UtcNow, Message = formatted, Version = Util.GetCurrentVersion() });
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
                await _hubConnection.InvokeAsync("LogClientError", new LogClientErrorRequest { ClientVersion = Util.GetCurrentVersion(), Report = errorReport });
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
            ResponseWrapper<List<Companion>> result = await InvokeHubAsync<List<Companion>>("GetContestLeaderBoard", new GetContestLeaderBoardRequest { ClientVersion = Util.GetCurrentVersion() });
            return result.IsSuccess ? result.Data : null;
        }

        /// <summary>Returns a single CompanioNita advice entry by id, or null if the call fails.</summary>
        public async Task<CompanioNitaAdvice> GetCompanionitaAdviceById(int adviceId)
        {
            ResponseWrapper<CompanioNitaAdvice> result = await InvokeHubAsync<CompanioNitaAdvice>("GetCompanioNitaAdviceById", new GetCompanioNitaAdviceByIdRequest { AdviceId = adviceId, LanguageCode = CultureService.GetCurrentCulture(), ClientVersion = Util.GetCurrentVersion() });
            return result.IsSuccess ? result.Data : null;
        }

        /// <summary>Returns a page of CompanioNita advice entries, or null if the call fails.</summary>
        public async Task<List<CompanioNitaAdvice>> GetCompanionitaAdvice(int start, int count)
        {
            ResponseWrapper<List<CompanioNitaAdvice>> result = await InvokeHubAsync<List<CompanioNitaAdvice>>("GetCompanioNitaAdvice", new GetCompanioNitaAdviceRequest { Start = start, Count = count, LanguageCode = CultureService.GetCurrentCulture(), ClientVersion = Util.GetCurrentVersion() });
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

                await foreach (string chunk in _hubConnection.StreamAsync<string>("StreamAskCompanioNitaAboutConversation", new StreamAskCompanioNitaAboutConversationRequest { LoginToken = _loginGuid, UserId = userId, ClientVersion = Util.GetCurrentVersion() }))
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
                ResponseWrapper<string> result = await InvokeHubAsync<string>("AskCompanioNita", new AskCompanioNitaRequest { LoginToken = _loginGuid, ThreadId = threadId, Message = i_message, ClientVersion = Util.GetCurrentVersion() });

                if (!result.IsSuccess && IsSubscriptionError(result.ErrorCode))
                {
                    // Subscription required, expired, inactive, or usage limit exceeded.
                    // InvalidCredentials is handled centrally by InvokeHubAsync, so it
                    // never reaches this branch.
                    RequestSubscription();
                    return $"⚠️ {result.Message}";
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

                await foreach (string chunk in _hubConnection.StreamAsync<string>("StreamAskCompanioNita", new StreamAskCompanioNitaRequest { LoginToken = _loginGuid, ThreadId = threadId, Message = i_message, ClientVersion = Util.GetCurrentVersion() }))
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
                ResponseWrapper<int> result = await InvokeHubAsync<int>("StartAdviceThread", new StartAdviceThreadRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
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
            ResponseWrapper<List<AdviceThread>> result = await InvokeHubAsync<List<AdviceThread>>("GetAdviceThreads", new GetAdviceThreadsRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
            return result.IsSuccess ? result.Data ?? [] : [];
        }

        /// <summary>Returns the question/answer exchanges of one CompanioNita advice thread, oldest first; empty list on failure.</summary>
        public async Task<List<AdviceExchange>> GetAdviceExchangesAsync(int threadId)
        {
            try
            {
                await Initialize();
                ResponseWrapper<List<AdviceExchange>> result = await InvokeHubAsync<List<AdviceExchange>>("GetAdviceExchanges", new GetAdviceExchangesRequest { LoginToken = _loginGuid, ThreadId = threadId, ClientVersion = Util.GetCurrentVersion() });
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
            ResponseWrapper<List<Advice>> result = await InvokeHubAsync<List<Advice>>("GetAdvice", new GetAdviceRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
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
            ResponseWrapper<Settings> result = await InvokeHubAsync<Settings>("GetSettings", new GetSettingsRequest { LanguageCode = CultureService.GetCurrentCulture(), ClientVersion = Util.GetCurrentVersion() });
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
            else if (loginResult.ErrorCode == ErrorCodes.InvalidCredentials)
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

                ResponseWrapper<UserDetails> result = await InvokeHubAsync<UserDetails>(
                    "Login",
                    new LoginRequest { Email = i_email, Password = i_password, ClientVersion = Util.GetCurrentVersion() },
                    HubInvokeOptions.LogFailures | HubInvokeOptions.PromptUpdateOnUpgradeRequired);
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
                return await InvokeHubAsync<bool>("AcceptTerms", new AcceptTermsRequest { LoginToken = _loginGuid, Version = version, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<bool> result = await InvokeHubAsync<bool>("GuaranteeConfirm", new GuaranteeConfirmRequest { VerificationCode = verificationCode, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<object> result = await InvokeHubAsync<object>("GuaranteeUser", new GuaranteeUserRequest { LoginToken = _loginGuid, Email = email, ImageData = imageData, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<Guid> result = await InvokeHubAsync<Guid>("UploadPhoto", new UploadPhotoRequest { LoginToken = _loginGuid, ImageData = imageData, ClientVersion = Util.GetCurrentVersion() });
                if (!result.IsSuccess)
                {
                    if (result.ErrorCode == ErrorCodes.InvalidCredentials)
                    {
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
                ResponseWrapper<object> result = await InvokeHubAsync<object>("GuaranteeEmail", new GuaranteeEmailRequest { LoginToken = _loginGuid, Email = email, ClientVersion = Util.GetCurrentVersion() });
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
            ResponseWrapper<bool> result = await InvokeHubAsync<bool>("AddIgnore", new AddIgnoreRequest { LoginToken = _loginGuid, UserId = userId, ClientVersion = Util.GetCurrentVersion() });
            return result.IsSuccess && result.Data;
        }

        /// <summary>Removes a user from the current user's ignore list; false on failure.</summary>
        public async Task<bool> RemoveIgnore(int userId)
        {
            ResponseWrapper<bool> result = await InvokeHubAsync<bool>("RemoveIgnore", new RemoveIgnoreRequest { LoginToken = _loginGuid, UserId = userId, ClientVersion = Util.GetCurrentVersion() });
            return result.IsSuccess && result.Data;
        }

        /// <summary>Reports a user for objectionable content.</summary>
        public async Task<ReportResult?> ReportUserAsync(ReportRequest request)
        {
            await Initialize();

            try
            {
                var result = await InvokeHubAsync<ReportResult>("ReportUser", new ReportUserRequest { LoginToken = _loginGuid, Report = request, ClientVersion = Util.GetCurrentVersion() });
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
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
                var result = await InvokeHubAsync<List<PendingReport>>("GetPendingReports", new GetPendingReportsRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
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
                var result = await InvokeHubAsync<bool>("ResolveReport", new ResolveReportRequest { LoginToken = _loginGuid, ReportId = reportId, Status = status, ClientVersion = Util.GetCurrentVersion() });
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
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
                var result = await InvokeHubAsync<bool>("SetMuteStatus", new SetMuteStatusRequest { LoginToken = _loginGuid, TargetUserId = targetUserId, IsMuted = isMuted, ClientVersion = Util.GetCurrentVersion() });
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
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
                ResponseWrapper<UserConversation> result = await InvokeHubAsync<UserConversation>("StartUserConversation", new StartUserConversationRequest { LoginToken = _loginGuid, UserId = userId, ClientVersion = Util.GetCurrentVersion() });
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
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
            ResponseWrapper<List<GuaranteedUser>> result = await InvokeHubAsync<List<GuaranteedUser>>("GetGuaranteedUsers", new GetGuaranteedUsersRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
            return result.IsSuccess ? result.Data : new List<GuaranteedUser>();
        }


        /// <summary>Validates an email verification code; false if empty, invalid, or on error.</summary>
        public async Task<bool> CheckVerificationCode(string i_verificationCode)
        {
            if (string.IsNullOrEmpty(i_verificationCode)) return false;

            try
            {
                await Initialize();
                ResponseWrapper<object> result = await InvokeHubAsync<object>("CheckVerificationCode", new CheckVerificationCodeRequest { VerificationCode = i_verificationCode, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<object> result = await InvokeHubAsync<object>("ResetPassword", new ResetPasswordRequest { VerificationCode = i_verificationCode, NewPassword = i_newPassword, ClientVersion = Util.GetCurrentVersion() });
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
            ResponseWrapper<List<UserImage>> result = await InvokeHubAsync<List<UserImage>>("GetUserImages", new GetUserImagesRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
            return result.Data ?? [];
        }


        /// <summary>Returns messages the current user has ignored; empty list on failure.</summary>
        public async Task<List<UserMessage>> GetIgnoredMessagesAsync()
        {
            ResponseWrapper<List<UserMessage>> result = await InvokeHubAsync<List<UserMessage>>("GetIgnoredMessages", new GetIgnoredMessagesRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<List<Companion>>(
                    "FindCompanions",
                    new FindCompanionsRequest
                    {
                        LoginToken = _loginGuid,
                        CisMale = cisMale,
                        CisFemale = cisFemale,
                        Other = other,
                        TransMale = transMale,
                        TransFemale = transFemale,
                        Cities = cities,
                        AgeMin = ageFrom ?? 18,
                        AgeMax = ageTo ?? 99,
                        ShowIgnoredUsers = showIgnoredUsers,
                        ClientVersion = Util.GetCurrentVersion()
                    }
                );
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
                await InvokeHubVoidAsync("RequestPasswordReset", new RequestPasswordResetRequest { Email = i_email, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<bool> result = await InvokeHubAsync<bool>("ResendVerificationEmail", new ResendVerificationEmailRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
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
            ResponseWrapper<List<UserConversation>> result = await InvokeHubAsync<List<UserConversation>>("GetUserConversations", new GetUserConversationsRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
            return result.Data;
        }

        /// <summary>Returns the message thread with a specific user; null on failure or transient timeout.</summary>
        public async Task<List<UserMessage>> GetMessagesWithUserAsync(int userId)
        {
            ResponseWrapper<List<UserMessage>> result = await InvokeHubAsync<List<UserMessage>>("GetMessagesWithUser", new GetMessagesWithUserRequest { LoginToken = _loginGuid, UserId = userId, ClientVersion = Util.GetCurrentVersion() });
            return result.Data;
        }

        public async Task<int> SendMessageAsync(int userId, string messageText)
        {
            try
            {
                await Initialize();
                ResponseWrapper<int> result = await InvokeHubAsync<int>("SendMessage", new SendMessageRequest { LoginToken = _loginGuid, UserId = userId, MessageText = messageText, ClientVersion = Util.GetCurrentVersion() });
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
                var response = await InvokeHubAsync<bool>("RemoveGuarantee", new RemoveGuaranteeRequest { LoginToken = _loginGuid, ImageId = imageId, ClientVersion = Util.GetCurrentVersion() });

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
                ResponseWrapper<string> result = await InvokeHubAsync<string>("GetLinkPayload", new GetLinkPayloadRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
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
                ResponseWrapper<LinkedUser> result = await InvokeHubAsync<LinkedUser>("RedeemQrLink", new RedeemQrLinkRequest { LoginToken = _loginGuid, Code = code, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<object> result = await InvokeHubAsync<object>("LinkEmail", new LinkEmailRequest { LoginToken = _loginGuid, Email = email, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<UserDetails> result = await InvokeHubAsync<UserDetails>("ConfirmEmailLink", new ConfirmEmailLinkRequest { VerificationCode = verificationCode, ClientVersion = Util.GetCurrentVersion() });
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
                return await InvokeHubAsync<string>("RejectEmailLink", new RejectEmailLinkRequest { VerificationCode = verificationCode, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<List<LinkedUser>> result = await InvokeHubAsync<List<LinkedUser>>("GetLinkedUsers", new GetLinkedUsersRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
                if (!result.IsSuccess)
                {
                    if (result.ErrorCode == ErrorCodes.InvalidCredentials)
                    {
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
                ResponseWrapper<object> result = await InvokeHubAsync<object>("UploadLinkPhoto", new UploadLinkPhotoRequest { LoginToken = _loginGuid, ConnectionId = connectionId, ImageData = imageData, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<object> result = await InvokeHubAsync<object>("DeleteUserPhoto", new DeleteUserPhotoRequest { LoginToken = _loginGuid, ImageId = imageId, ClientVersion = Util.GetCurrentVersion() });
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
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
                ResponseWrapper<object> result = await InvokeHubAsync<object>("SetLinkPhotoVisibility", new SetLinkPhotoVisibilityRequest { LoginToken = _loginGuid, ImageId = imageId, Visible = visible, ClientVersion = Util.GetCurrentVersion() });
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
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
                ResponseWrapper<object> result = await InvokeHubAsync<object>("ConfirmLinkPhoto", new ConfirmLinkPhotoRequest { LoginToken = _loginGuid, ImageId = imageId, ClientVersion = Util.GetCurrentVersion() });
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
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
                ResponseWrapper<object> result = await InvokeHubAsync<object>("RejectLinkPhoto", new RejectLinkPhotoRequest { LoginToken = _loginGuid, ImageId = imageId, ClientVersion = Util.GetCurrentVersion() });
                if (!result.IsSuccess && result.ErrorCode == ErrorCodes.InvalidCredentials)
                {
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
                ResponseWrapper<List<KarmaDesync>> result = await InvokeHubAsync<List<KarmaDesync>>("RecalculateKarma", new RecalculateKarmaRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<GuarantorMigrationResult> result = await InvokeHubAsync<GuarantorMigrationResult>("MigrateGuarantorData", new MigrateGuarantorDataRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<bool> result = await InvokeHubAsync<bool>(
                    "UpdateUserDetails",
                    new UpdateUserDetailsRequest { LoginToken = _loginGuid, UserDetails = userDetails, ClientVersion = Util.GetCurrentVersion() }
                );
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
                return await InvokeHubAsync<string>("RequestEmailChange", new RequestEmailChangeRequest { LoginToken = _loginGuid, NewEmail = newEmail, ClientVersion = Util.GetCurrentVersion() });
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
                return await InvokeHubAsync<bool>("ConfirmEmailChange", new ConfirmEmailChangeRequest { LoginToken = _loginGuid, VerificationCode = verificationCode, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<bool> result = await InvokeHubAsync<bool>(
                    "DeleteProfile",
                    new DeleteProfileRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() }
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
                var result = await InvokeHubAsync<bool>(
                    "UpdateImageVisibility",
                    new UpdateImageVisibilityRequest { LoginToken = _loginGuid, ImageId = imageId, IsVisible = isVisible, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<bool>(
                    "UpdateReviewVisibility",
                    new UpdateReviewVisibilityRequest { LoginToken = _currentUser.LoginToken.ToString(), ImageId = imageId, IsPublic = isPublic, ClientVersion = Util.GetCurrentVersion() }
                );
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
                var result = await InvokeHubAsync<bool>(
                    "UpdateImageReview",
                    new UpdateImageReviewRequest { LoginToken = _loginGuid, ImageId = imageId, Rating = rating, Review = review, ClientVersion = Util.GetCurrentVersion() }
                );
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
            ResponseWrapper<bool> result = await InvokeHubAsync<bool>("SetConnectionReview", new SetConnectionReviewRequest { LoginToken = _loginGuid, ConnectionId = connectionId, Rating = rating, Review = review, ClientVersion = Util.GetCurrentVersion() });
            return result.IsSuccess && result.Data;
        }

        /// <summary>Toggles whether a review about the current user is publicly visible; false on failure.</summary>
        public async Task<bool> SetConnectionReviewVisibility(int connectionId, bool isVisible)
        {
            ResponseWrapper<bool> result = await InvokeHubAsync<bool>("SetConnectionReviewVisibility", new SetConnectionReviewVisibilityRequest { LoginToken = _loginGuid, ConnectionId = connectionId, IsVisible = isVisible, ClientVersion = Util.GetCurrentVersion() });
            return result.IsSuccess && result.Data;
        }



        public async Task<string> TriggerMaintenanceManually()
        {
            try
            {
                // Call the hub method to trigger maintenance
                ResponseWrapper<string> result = await InvokeHubAsync<string>("TriggerMaintenanceManually", new TriggerMaintenanceManuallyRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<string> result = await InvokeHubAsync<string>("RunTestSuite", new RunTestSuiteRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
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

            ResponseWrapper<List<Country>> result = await InvokeHubAsync<List<Country>>("GetCountries", new GetCountriesRequest { Continent = continent, ClientVersion = Util.GetCurrentVersion() });
            return result.Data ?? new List<Country>();
        }
        public async Task<List<City>> GetNearbyCities()
        {
            ResponseWrapper<List<City>> result = await InvokeHubAsync<List<City>>("GetNearbyCities", new GetNearbyCitiesRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
            return result.Data ?? new List<City>();
        }
        public async Task<List<City>> GetCities(string country, string searchTerm)
        {
            ResponseWrapper<List<City>> result = await InvokeHubAsync<List<City>>("GetCities", new GetCitiesRequest { Country = country, SearchTerm = searchTerm, ClientVersion = Util.GetCurrentVersion() });
            return result.Data ?? new List<City>();
        }
        public async Task<City> GetCity(int geonameid)
        {
            ResponseWrapper<City> result = await InvokeHubAsync<City>("GetCity", new GetCityRequest { LoginToken = _loginGuid, Geonameid = geonameid, ClientVersion = Util.GetCurrentVersion() });
            return result.Data;
        }
        public async Task<List<City>> GetNearestCities(double latitude, double longitude)
        {
            ResponseWrapper<List<City>> result = await InvokeHubAsync<List<City>>("GetNearestCities", new GetNearestCitiesRequest { LoginToken = _loginGuid, Latitude = latitude, Longitude = longitude, ClientVersion = Util.GetCurrentVersion() });
            return result.Data ?? new List<City>();
        }
        public async Task<CheckEmailResult> CheckEmailExists(string email)
        {
            try
            {
                await Initialize();
                ResponseWrapper<CheckEmailResult> result = await InvokeHubAsync<CheckEmailResult>("CheckEmailExists", new CheckEmailExistsRequest { Email = email, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<bool> result = await InvokeHubAsync<bool>("CreateNewUser", new CreateNewUserRequest { Email = email, Password = password, ClientVersion = Util.GetCurrentVersion() });
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
                await InvokeHubVoidAsync("ReceiveFeedback", new ReceiveFeedbackRequest { LoginToken = _loginGuid, FeedbackText = feedbackText, FeedbackDebugInfo = debugInfo, ClientVersion = Util.GetCurrentVersion() });
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
                ResponseWrapper<UserDetails> result = await InvokeHubAsync<UserDetails>("LoginWithGoogle", new LoginWithGoogleRequest { Code = code, CodeVerifier = code_verifier, RedirectUri = redirect_uri, ClientVersion = Util.GetCurrentVersion() });
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

                ResponseWrapper<UserDetails> result = await InvokeHubAsync<UserDetails>(
                    "LoginWithApple",
                    new LoginWithAppleRequest { Code = code, RedirectUri = redirect_uri, FirstName = firstName, LastName = lastName, ClientVersion = Util.GetCurrentVersion() });
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

                ResponseWrapper<UserDetails> result = await InvokeHubAsync<UserDetails>("LoginWithFacebook", new LoginWithFacebookRequest { Code = code, CodeVerifier = code_verifier, RedirectUri = redirect_uri, ClientVersion = Util.GetCurrentVersion() });
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

                ResponseWrapper<UserDetails> result = await InvokeHubAsync<UserDetails>("LoginWithTwitter", new LoginWithTwitterRequest { Code = code, CodeVerifier = code_verifier, RedirectUri = redirect_uri, ClientVersion = Util.GetCurrentVersion() });
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

                ResponseWrapper<UserDetails> result = await InvokeHubAsync<UserDetails>("LoginWithMicrosoft", new LoginWithMicrosoftRequest { Code = code, CodeVerifier = code_verifier, RedirectUri = redirect_uri, ClientVersion = Util.GetCurrentVersion() });
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
            return await InvokeHubAsync<OAuthConfig>("GetOAuthConfig", new GetOAuthConfigRequest { ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<List<UserDetails>>("AdminGetFlaggedProfiles", new AdminGetFlaggedProfilesRequest { LoginToken = _loginGuid, Offset = offset, Count = count, SearchTerm = searchTerm, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<UserDetails>("AdminGetProfileForAudit", new AdminGetProfileForAuditRequest { LoginToken = _loginGuid, UserId = userId, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<bool>("AdminUpdateProfile", new AdminUpdateProfileRequest { LoginToken = _loginGuid, UserDetails = userDetails, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<bool>("AdminUpdateUserAttributes", new AdminUpdateUserAttributesRequest { LoginToken = _loginGuid, Attributes = attributes, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<SiteStats>("AdminGetSiteStats", new AdminGetSiteStatsRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<bool>("AdminDeletePhoto", new AdminDeletePhotoRequest { LoginToken = _loginGuid, UserId = userId, ImageId = imageId, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<List<EventBadge>>("GetUserBadges", new GetUserBadgesRequest { LoginToken = _loginGuid, TargetUserId = targetUserId, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<List<EventBadge>>("AdminListEventBadges", new AdminListEventBadgesRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
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
                var result = award
                    ? await InvokeHubAsync<bool>(method, new AdminAwardEventBadgeRequest { LoginToken = _loginGuid, TargetUserId = targetUserId, BadgeId = badgeId, ClientVersion = Util.GetCurrentVersion() })
                    : await InvokeHubAsync<bool>(method, new AdminRevokeEventBadgeRequest { LoginToken = _loginGuid, TargetUserId = targetUserId, BadgeId = badgeId, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<BroadcastResult>("AdminSendBroadcastNotification", new AdminSendBroadcastNotificationRequest { LoginToken = _loginGuid, Title = title, Body = body, Url = url, TargetEmail = targetEmail, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<List<OrphanedImage>>("AdminFindOrphanedImages", new AdminFindOrphanedImagesRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<int>("AdminDeleteOrphanedImages", new AdminDeleteOrphanedImagesRequest { LoginToken = _loginGuid, ImageGuids = imageGuids, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<bool>("AdminDismissProfile", new AdminDismissProfileRequest { LoginToken = _loginGuid, UserId = userId, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<bool>("AdminDeleteProfile", new AdminDeleteProfileRequest { LoginToken = _loginGuid, UserId = userId, ClientVersion = Util.GetCurrentVersion() });
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
                var result = await InvokeHubAsync<string>("AdminCheckPhoto", new AdminCheckPhotoRequest { LoginToken = _loginGuid, ImageGuid = imageGuid, ClientVersion = Util.GetCurrentVersion() });
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
                    "AdminCheckAllPhotos", new AdminCheckAllPhotosRequest { LoginToken = _loginGuid, ClientVersion = Util.GetCurrentVersion() }, cancellationToken))
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
