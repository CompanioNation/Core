using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CompanioNation.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;


namespace CompanioNationPWA
{

    public class CompanioNationSignalRClient
    {
        // Define an event that MainLayout can subscribe to
        public event Action OnLoginRequested;
        public event Action OnHubConnecting;
        public event Action OnHubConnected;
        public event Action OnHubDisconnected;
        public event Action OnStateHasChanged;
        public event Action OnInstalling;
        public async Task RequestLogin()
        {
            // This is called when the login times out, so we should cancel the push subscription
            await _jsRuntime.InvokeVoidAsync("window.unregisterPush");

            // Trigger the Login event
            OnLoginRequested?.Invoke();
        }

        private class LogEntry
        {
            public DateTime timestamp { get; set; }
            public string message { get; set; }
            public string version { get; set; }
        }


        private readonly IJSRuntime _jsRuntime;
        private readonly NavigationManager _navigationManager;  // TODO - i don't think I technically need this
        private readonly IConfiguration _configuration;
        private HubConnection? _hubConnection;

        // The _loginGuid stores the login state token so that we don't have to keep passing in the username and password
        private string? _loginGuid = null;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1); // Keep this static as it's managing shared access

        private string? _currentVersion = "";

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
            await _semaphore.WaitAsync();
            int attempt = 0;

            try
            {
                if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                {
                    return; // Already connected
                }

                if (_hubConnection != null)
                {
                    Console.WriteLine("***Hub Connection Was MYSTERIOUSLY:" + _hubConnection.State);

                    if (_hubConnection.State == HubConnectionState.Disconnected)
                    {
                        try
                        {
                            await _hubConnection.StartAsync();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Reconnection failed: {ex.Message}");
                        }
                    }

                    if (_hubConnection.State == HubConnectionState.Connecting || _hubConnection.State == HubConnectionState.Reconnecting)
                    {
                        // Wait until the connection is fully reconnected
                        var tcs = new TaskCompletionSource();

                        Task ReconnectedHandler(string? connectionId)
                        {
                            tcs.SetResult();
                            _hubConnection.Reconnected -= ReconnectedHandler; // Remove the handler correctly
                            return Task.CompletedTask;
                        }

                        _hubConnection.Reconnected += ReconnectedHandler;

                        // Wait for reconnection or timeout after a specific period
                        await Task.WhenAny(tcs.Task, Task.Delay(5000)); // 5 seconds timeout
                    }
                    if (_hubConnection.State == HubConnectionState.Connected)
                    {
                        OnHubConnected?.Invoke();
                        return;
                    }

                    Console.WriteLine("****REBUILDING Hub From Scratch Because: Hub Connection STILL not connected: " + _hubConnection.State);
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                }

                // Build a new connection
                string url = GetHubUrl();
                Console.WriteLine($"***Building New Hub Connection*** on {url}");
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(url)
                    //.WithAutomaticReconnect()  // we use our own custom reconnection logic -- TODO maybe reconsider this if the built-in login works well
                    .Build();

                _hubConnection.Closed += async (error) =>
                {
                    Console.WriteLine("Connection closed and reconnection attempts failed.");
                    if (error != null)
                    {
                        Console.WriteLine($"Error: {error.Message}");
                    }
                    OnHubDisconnected?.Invoke();

                    // Automatically attempt to reconnect
                    Initialize();
                };

                _hubConnection.Reconnecting += (connectionId) =>
                {
                    OnHubConnecting?.Invoke();
                    return Task.CompletedTask;
                };

                _hubConnection.Reconnected += async (connectionId) =>
                {
                    Console.WriteLine("RECONNECTED to the SignalR Hub.");
                    OnHubConnected?.Invoke();
                    await Connect(); // Revalidate version and session
                };
                // Obviously we can't do this automatically in the background because of browser security issues
                //_hubConnection.On("RenewPushRegistration", async () => { RenewPushRegistration(); });
                /*
                _hubConnection.On<string, int, string>("ReceiveMessage", async (string message, int userId, string userName) =>
                {
                    try
                    {
                        // Update the unread message count
                        if (_currentUser != null)
                        {
                            _currentUser.UnreadMessagesCount += 1;
                            OnStateHasChanged?.Invoke();  // Make sure the UI is updated with the new unread message count
                        }

                        // Send a message to the Service Worker to display a notification
                        var payload = new
                        {
                            type = "SHOW_NOTIFICATION",
                            payload = new
                            {
                                userId = userId,
                                title = "New Message from " + userName,
                                options = new
                                {
                                    body = message,
                                    icon = "/favicon.png",
                                    badge = "/favicon.png"
                                }
                            }
                        };

                        // Send message to the Service Worker
                        await _jsRuntime.InvokeVoidAsync("navigator.serviceWorker.controller.postMessage", payload);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error displaying notification: " + ex.Message);
                    }
                });
                */

                // Trigger an event to indicate that the hub is connecting
                OnHubConnecting?.Invoke();

                // Adaptive retry logic
                while (true)
                {
                    attempt++;
                    try
                    {
                        await _hubConnection.StartAsync();
                        Debug.Assert(_hubConnection.State == HubConnectionState.Connected);
                        Console.WriteLine("CONNECTED to the SignalR Hub!");
                        OnHubConnected?.Invoke();
                        await Connect();
                        break;
                    }
                    catch
                    {
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

        /*
        private async Task RenewPushRegistration()
        {
            try
            {
                _pushTokenUpdated = false;
                await _jsRuntime.InvokeVoidAsync("window.unregisterPush");
                Console.WriteLine("Push subscription unregistered successfully.");
                PushToken = await _jsRuntime.InvokeAsync<string>("window.registerPush");
                await UpdatePushToken(PushToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to renew push subscription: {ex.Message}");
            }

        }
        */

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
                ResponseWrapper<ConnectResult> result = await _hubConnection.InvokeAsync<ResponseWrapper<ConnectResult>>("Connect", _loginGuid);
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

                            // Invalid login token, so clear it and the local storage
                            _loginGuid = null;
                            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "loginGuid");

                            // We were logged in, but upon reconnection the login token is now invalid
                            // Show the Login Prompt
                            await RequestLogin();
                        }
                    }
                }


                _currentVersion = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "_currentVersion");
                string serverVersion = result.Version;

                Console.WriteLine("PWA Version: " + await GetPWAVersion());
                Console.WriteLine("Current Version: " + _currentVersion);
                Console.WriteLine("Server Version: " + serverVersion);

                if (_currentVersion == null) _currentVersion = serverVersion;
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "_currentVersion", serverVersion);

                if (serverVersion != _currentVersion)
                {
                    // Show the "Installing..." indicator
                    OnInstalling?.Invoke();

                    Console.WriteLine("Installing New Version in Background...");
                    // No need to confirm because this is all done in the background, and then just does a reload at the end
                    //  once the new version is fully installed and activated
                    await _jsRuntime.InvokeVoidAsync("window.installNewVersion");
                }

                // Asynchronously dump the local log if there is one
                DumpLocalLog();

                return true;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting version: {ex.Message} {ex.StackTrace}");
                await LogError(ex, "REALLY UNEXPECTED Connecting Exception");

                if (_currentUser == null)
                {
                    // TODO - CONFIRM - I don't think the below is necessary and it causes annoying logouts sometimes for no good reason
                    // Log the user out so the app is in a consistent state
                    //_loginGuid = null;
                    //await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "loginGuid");
                }
                return false;
            }
        }

        public async Task UpdatePushToken(string pushToken)
        {
            try
            {
                Console.WriteLine("Updating push token to: " + pushToken);
                ResponseWrapper<bool> result = await _hubConnection.InvokeAsync<ResponseWrapper<bool>>("UpdatePushToken", _loginGuid, pushToken);
            }
            catch (Exception ex)
            {
                await LogError(ex);
            }
        }

        private async Task AppendToLocalLog(DateTime i_timestamp, string i_message, string i_version)
        {
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
                if (logEntries == null) return;

                await _hubConnection.InvokeAsync("LogError", DateTime.UtcNow, "====== DUMPING LOCAL LOG ======", _currentVersion);

                while (logEntries.Count > 0)
                {
                    await _hubConnection.InvokeAsync("LogError", logEntries[0].timestamp, logEntries[0].message, logEntries[0].version);
                    logEntries.RemoveAt(0);
                    string updatedLogEntriesJson = JsonSerializer.Serialize(logEntries);
                    await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "errorLog", updatedLogEntriesJson);
                }

                await _hubConnection.InvokeAsync("LogError", DateTime.UtcNow, "====== LOCAL LOG DUMP COMPLETE ======", _currentVersion);
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "errorLog");
            }
            catch (Exception ex)
            {
                await AppendToLocalLog(DateTime.UtcNow, ex.Message + ex.StackTrace, _currentVersion);
            }
        }

        public async Task LogError<T>(ResponseWrapper<T> error)
        {
            await LogError("(" + error.ErrorCode.ToString() + " 0x" + error.ErrorCode.ToString("X8") + ")" + error.Message);
        }
        public async Task LogError(Exception i_ex)
        {
            await LogError(i_ex, "");
        }

        public async Task LogError(Exception i_ex, string i_additionalInfo)
        {
            await LogError(i_additionalInfo + ":" + i_ex.Message + i_ex.StackTrace);
        }
        public async Task LogError(string i_message)
        {
            try
            {
                await Initialize();
                await _hubConnection.InvokeAsync("LogError", DateTime.UtcNow, i_message, _currentVersion);
            }
            catch (Exception ex)
            {
                await AppendToLocalLog(DateTime.UtcNow, i_message, _currentVersion);
                await AppendToLocalLog(DateTime.UtcNow, ex.Message + ex.StackTrace, _currentVersion);
            }
        }


        public async Task SetMessageCount(int messageCount)
        {
            if (_currentUser != null)
            {
                _currentUser.UnreadMessagesCount = messageCount;
                OnStateHasChanged?.Invoke();  // Make sure the UI is updated with the new unread message count
            }
        }

        public async Task<List<Companion>> GetContestLeaderBoard()
        {
            try
            {
                await Initialize(); // Ensure the SignalR connection is initialized
                ResponseWrapper<List<Companion>> leaderboard = await _hubConnection.InvokeAsync<ResponseWrapper<List<Companion>>>("GetContestLeaderBoard");
                if (leaderboard.IsSuccess) return leaderboard.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex);
            }
            return null;
        }
        public async Task<CompanioNitaAdvice> GetCompanionitaAdviceById(int adviceId)
        {
            try
            {
                await Initialize(); // Ensure the SignalR connection is initialized
                ResponseWrapper<CompanioNitaAdvice> advice = await _hubConnection.InvokeAsync<ResponseWrapper<CompanioNitaAdvice>>("GetCompanioNitaAdviceById", adviceId);
                if (advice.IsSuccess) return advice.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex);
            }
            return null;
        }
        public async Task<List<CompanioNitaAdvice>> GetCompanionitaAdvice(int start, int count)
        {
            try
            {
                await Initialize(); // Ensure the SignalR connection is initialized
                ResponseWrapper<List<CompanioNitaAdvice>> advice = await _hubConnection.InvokeAsync<ResponseWrapper<List<CompanioNitaAdvice>>>("GetCompanioNitaAdvice", start, count);
                if (advice.IsSuccess) return advice.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex);
            }
            return null;
        }

        public async Task<bool> AskCompanioNitaAboutConversation(int userId)
        {
            try
            {
                await Initialize(); // Ensure the SignalR connection is initialized
                ResponseWrapper<int> result = await _hubConnection.InvokeAsync<ResponseWrapper<int>>("AskCompanioNitaAboutConversation", _loginGuid, userId);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                await LogError(ex);
                return false;
            }
        }
        public async Task<string> AskCompanioNita(string i_message)
        {
            try
            {
                await Initialize();
                ResponseWrapper<string> result = await _hubConnection.InvokeAsync<ResponseWrapper<string>>("AskCompanioNita", _loginGuid, i_message);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex);
                return "ERROR: " + ex.Message;
            }
        }

        public async Task<List<Advice>> GetAdvice()
        {
            try
            {
                await Initialize();
                ResponseWrapper<List<Advice>> result = await _hubConnection.InvokeAsync<ResponseWrapper<List<Advice>>>("GetAdvice", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex);
                return null;
            }
        }

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


        public async Task<Settings> GetSettingsAsync()
        {
            try
            {
                await Initialize();

                // Call the SignalR hub method to get settings
                ResponseWrapper<Settings> result = await _hubConnection.InvokeAsync<ResponseWrapper<Settings>>("GetSettings");
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "GetSettingsAsync()");
                return null;
            }
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
                    // send the push token to the server if it push notifications are enabled
                    string pushToken = await _jsRuntime.InvokeAsync<string>("window.registerPush", Util.VapidPublicKey);
                    UpdatePushToken(pushToken);
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
        }
        public async Task<ResponseWrapper<UserDetails>> Login(string i_email, string i_password)
        {
            try
            {
                await Initialize();

                ResponseWrapper<UserDetails> result = await _hubConnection.InvokeAsync<ResponseWrapper<UserDetails>>("Login", i_email, i_password);
                await DoLogin(result);
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "Login()");
                return ResponseWrapper<UserDetails>.Fail(ex.HResult, ex.Message);
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
                ResponseWrapper<bool> result = await _hubConnection.InvokeAsync<ResponseWrapper<bool>>("GuaranteeConfirm", verificationCode);
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
                ResponseWrapper<object> result = await _hubConnection.InvokeAsync<ResponseWrapper<object>>("GuaranteeUser", _loginGuid, email, imageData);
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
                await Initialize(); // Ensure the connection is initialized


                const long maxFileSize = 10485760; // 10 MB

                if (file.Size > maxFileSize)
                {
                    // File too big
                    return (-1, Guid.Empty);
                }

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

                // Call the SignalR hub method to upload the photo
                ResponseWrapper<Guid> result = await _hubConnection.InvokeAsync<ResponseWrapper<Guid>>("UploadPhoto", _loginGuid, imageData);
                if (!result.IsSuccess)
                {
                    if (result.ErrorCode == 100000)
                    {
                        await RequestLogin();
                        return (-10, Guid.Empty);
                    }
                    else if (result.ErrorCode == 200001)
                    {
                        // No face detected in the photo
                        return (-3, Guid.Empty);
                    }
                    else
                    {
                        // Some other error
                        return (-4, Guid.Empty);
                    }
                }
                return (0, result.Data);
            }
            catch (Exception ex)
            {
                await LogError(ex, "UploadPhotoAsync()");
                return (ex.HResult, Guid.Empty); // Return an empty GUID on failure
            }
        }
        public async Task<int> GuaranteeUser(string email)
        {
            await Initialize(); // Make sure the connection is initialized

            try
            {
                ResponseWrapper<object> result = await _hubConnection.InvokeAsync<ResponseWrapper<object>>("GuaranteeEmail", _loginGuid, email);
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

        public async Task<bool> AddIgnore(int userId)
        {
            await Initialize(); // Ensure the connection to SignalR Hub is established

            try
            {
                // Call the SignalR Hub method to get user details
                ResponseWrapper<bool> result = await _hubConnection.InvokeAsync<ResponseWrapper<bool>>("AddIgnore", _loginGuid, userId);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                    return false;
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "AddIgnore()");
                return false; // Return null or handle error appropriately
            }
        }
        public async Task<bool> RemoveIgnore(int userId)
        {
            await Initialize(); // Ensure the connection to SignalR Hub is established

            try
            {
                // Call the SignalR Hub method to get user details
                ResponseWrapper<bool> result = await _hubConnection.InvokeAsync<ResponseWrapper<bool>>("RemoveIgnore", _loginGuid, userId);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                    return false;
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "RemoveIgnore()");
                return false; // Return null or handle error appropriately
            }
        }

        public async Task<UserConversation> StartUserConversationAsync(int userId)
        {
            await Initialize(); // Ensure the connection to SignalR Hub is established

            try
            {
                // Call the SignalR Hub method to get user details
                ResponseWrapper<UserConversation> result = await _hubConnection.InvokeAsync<ResponseWrapper<UserConversation>>("StartUserConversation", _loginGuid, userId);
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

        public async Task<List<GuaranteedUser>> GetGuaranteedUsersAsync()
        {
            await Initialize(); // Ensure the connection is ready

            try
            {
                // Call the hub method to get the list of guaranteed users
                ResponseWrapper<List<GuaranteedUser>> result = await _hubConnection.InvokeAsync<ResponseWrapper<List<GuaranteedUser>>>("GetGuaranteedUsers", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "Error in GetGuaranteedUsersAsync");
                return new List<GuaranteedUser>(); // Return an empty list on failure
            }
        }


        public async Task<bool> CheckVerificationCode(string i_verificationCode)
        {
            if (string.IsNullOrEmpty(i_verificationCode)) return false;

            try
            {
                await Initialize();
                ResponseWrapper<object> result = await _hubConnection.InvokeAsync<ResponseWrapper<object>>("CheckVerificationCode", i_verificationCode);
                return result.IsSuccess;
            }
            catch (Exception ex)
            {
                await LogError(ex, "ResetPassword()");
                return false;
            }
        }
        public async Task<bool> ResetPassword(string i_verificationCode, string i_newPassword)
        {
            try
            {
                await Initialize();
                ResponseWrapper<object> result = await _hubConnection.InvokeAsync<ResponseWrapper<object>>("ResetPassword", i_verificationCode, i_newPassword);
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


        public async Task<List<UserImage>> GetUserImagesAsync()
        {
            try
            {
                await Initialize();

                // Call the SignalR hub method to get user images
                ResponseWrapper<List<UserImage>> result = await _hubConnection.InvokeAsync<ResponseWrapper<List<UserImage>>>("GetUserImages", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "GetUserImagesAsync()");
                return new List<UserImage>(); // Return an empty list if there's an error
            }
        }


        public async Task<List<UserMessage>> GetIgnoredMessagesAsync()
        {
            try
            {
                await Initialize();

                // Call the SignalR hub method to get ignored messages
                ResponseWrapper<List<UserMessage>> result = await _hubConnection.InvokeAsync<ResponseWrapper<List<UserMessage>>>("GetIgnoredMessages", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "GetIgnoredMessagesAsync()");
                return new List<UserMessage>(); // Return an empty list if there's an error
            }
        }


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
            await Initialize();

            try
            {
                // Call SignalR method
                var result = await _hubConnection.InvokeAsync<ResponseWrapper<List<Companion>>>(
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

        public async Task<bool> RequestNewVerificationCode(string i_email)
        {
            try
            {
                await Initialize();
                // Call the hub method to request a new verification code
                await _hubConnection.InvokeAsync("RequestNewVerificationCode", i_email);
                return true; // Always return true regardless of the internal success to avoid information leakage
            }
            catch (Exception ex)
            {
                await LogError(ex, "RequestNewVerificationCode()");
                return false; // Return false if an error occurs, without exposing specific details
            }
        }
        public async Task<List<UserConversation>> GetUserConversationsAsync()
        {
            await Initialize(); // Ensure the SignalR connection is initialized
            ResponseWrapper<List<UserConversation>> result = await _hubConnection.InvokeAsync<ResponseWrapper<List<UserConversation>>>("GetUserConversations", _loginGuid);
            if (!result.IsSuccess && result.ErrorCode == 100000)
            {
                await RequestLogin();
            }
            return result.Data;
        }

        public async Task<List<UserMessage>> GetMessagesWithUserAsync(int userId)
        {
            await Initialize(); // Ensure the SignalR connection is initialized
            ResponseWrapper<List<UserMessage>> result = await _hubConnection.InvokeAsync<ResponseWrapper<List<UserMessage>>>("GetMessagesWithUser", _loginGuid, userId);
            if (!result.IsSuccess && result.ErrorCode == 100000)
            {
                await RequestLogin();
            }
            return result.Data;
        }

        public async Task<int> SendMessageAsync(int userId, string messageText)
        {
            await Initialize(); // Ensure the SignalR connection is initialized
            ResponseWrapper<int> result = await _hubConnection.InvokeAsync<ResponseWrapper<int>>("SendMessage", _loginGuid, userId, messageText);
            if (!result.IsSuccess && result.ErrorCode == 100000)
            {
                await RequestLogin();
            }
            return result.Data;
        }

        public async Task<bool> RemoveGuaranteeAsync(int imageId)
        {
            await Initialize(); // Ensure the SignalR connection is initialized

            try
            {
                // Call the hub method to remove the guarantee using the ImageID
                var response = await _hubConnection.InvokeAsync<ResponseWrapper<bool>>("RemoveGuarantee", _loginGuid, imageId);

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


        public async Task<bool> UpdateUserDetailsAsync(UserDetails userDetails)
        {
            try
            {
                await Initialize(); // Ensure the connection is initialized

                // Call the SignalR hub method to update user details by passing the UserDetails object
                ResponseWrapper<bool> result = await _hubConnection.InvokeAsync<ResponseWrapper<bool>>(
                    "UpdateUserDetails",
                    _loginGuid,
                    userDetails
                );

                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                _currentUser = userDetails;
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "Error in UpdateUserDetailsAsync()");
                return false; // Return false if an error occurs
            }
        }



        // Method to update the visibility of an image
        public async Task UpdateImageVisibility(int imageId, bool isVisible)
        {
            try
            {
                await Initialize(); // Ensure SignalR connection is established

                // Call the SignalR hub method to update image visibility
                var result = await _hubConnection.InvokeAsync<ResponseWrapper<bool>>(
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
                await Initialize(); // Ensure the connection is initialized

                // Call the SignalR hub method to update the visibility
                var result = await _hubConnection.InvokeAsync<ResponseWrapper<bool>>(
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
                await Initialize(); // Ensure the connection is initialized

                // Call the SignalR hub method to update the rating and review
                var result = await _hubConnection.InvokeAsync<ResponseWrapper<bool>>(
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



        public async Task<string> TriggerMaintenanceManually()
        {
            try
            {
                await Initialize();
                // Call the hub method to trigger maintenance
                ResponseWrapper<string> result = await _hubConnection.InvokeAsync<ResponseWrapper<string>>("TriggerMaintenanceManually", _loginGuid);
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
                await Initialize();
                ResponseWrapper<string> result = await _hubConnection.InvokeAsync<ResponseWrapper<string>>("RunTestSuite", _loginGuid);
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

            try
            {
                await Initialize(); // Ensure the SignalR connection is initialized
                ResponseWrapper<List<Country>> result = await _hubConnection.InvokeAsync<ResponseWrapper<List<Country>>>("GetCountries", continent);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "GetCountries()");
                return new List<Country>(); // Return an empty list if there's an error
            }
        }
        public async Task<List<City>> GetNearbyCities()
        {
            try
            {
                await Initialize(); // Ensure the SignalR connection is initialized
                ResponseWrapper<List<City>> result = await _hubConnection.InvokeAsync<ResponseWrapper<List<City>>>("GetNearbyCities", _loginGuid);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "GetNearbyCities()");
                return new List<City>(); // Return an empty list if there's an error
            }
        }
        public async Task<List<City>> GetCities(string country, string searchTerm)
        {
            try
            {
                await Initialize(); // Ensure the SignalR connection is initialized
                ResponseWrapper<List<City>> result = await _hubConnection.InvokeAsync<ResponseWrapper<List<City>>>("GetCities", country, searchTerm);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "GetCities()");
                return new List<City>(); // Return an empty list if there's an error
            }
        }
        public async Task<City> GetCity(int geonameid)
        {
            try
            {
                await Initialize(); // Ensure the SignalR connection is initialized
                ResponseWrapper<City> result = await _hubConnection.InvokeAsync<ResponseWrapper<City>>("GetCity", _loginGuid, geonameid);
                if (!result.IsSuccess && result.ErrorCode == 100000)
                {
                    await RequestLogin();
                }
                return result.Data;
            }
            catch (Exception ex)
            {
                await LogError(ex, "GetCity()");
                return null; // Return null if there's an error
            }
        }
        public async Task<CheckEmailResult> CheckEmailExists(string email)
        {
            try
            {
                await Initialize();
                ResponseWrapper<CheckEmailResult> result = await _hubConnection.InvokeAsync<ResponseWrapper<CheckEmailResult>>("CheckEmailExists", email);
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
                ResponseWrapper<bool> result = await _hubConnection.InvokeAsync<ResponseWrapper<bool>>("CreateNewUser", email, password);
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
                await _hubConnection.InvokeAsync("ReceiveFeedback", feedbackText);
            }
            catch (Exception ex)
            {
                await LogError(ex, "SendFeedback");
            }
        }

        public async Task<ResponseWrapper<UserDetails>> LoginWithGoogle(string code, string code_verifier, string redirect_uri)
        {
            try
            {
                await Initialize(); // Ensure the SignalR connection is initialized

                // Call the SignalR hub method to log in with Google
                ResponseWrapper<UserDetails> result = await _hubConnection.InvokeAsync<ResponseWrapper<UserDetails>>("LoginWithGoogle", code, code_verifier, redirect_uri);
                await DoLogin(result);
                return result;
            }
            catch (Exception ex)
            {
                await LogError(ex, "LoginWithGoogle()");
                return ResponseWrapper<UserDetails>.Fail(ex.HResult, ex.Message);
            }
        }

    }
}
