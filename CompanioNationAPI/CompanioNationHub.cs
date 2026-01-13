using CompanioNation.Shared;
using Microsoft.AspNetCore.SignalR;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;


namespace CompanioNationAPI
{


    public class CompanioNationHub : Hub
    {
        private readonly Database _database;
        private readonly CompanioNita _companioNita;
        private readonly MaintenanceEventService _maintenanceEventService; // Add this line

        public CompanioNationHub(Database database, CompanioNita companioNita, MaintenanceEventService maintenanceEventService)
        {
            _database = database;
            _companioNita = companioNita;
            _maintenanceEventService = maintenanceEventService; // Initialize the service
        }

        private async Task SetSignalRGroupId(int userId)
        {
            // I'm using SignalR groups because sending to specific User functionality wasn't working
            // I didn't figure out precisely why this is, but I assume it has to do with the fact that I
            //   ripped out all of the user authentication stuff because i'm using custom db users
            await Groups.AddToGroupAsync(Context.ConnectionId, userId.ToString());
        }
        private string GetClientIpAddress()
        {
            return Context.GetHttpContext()?.Connection?.RemoteIpAddress?.ToString();
        }

        public async Task<ResponseWrapper<UserDetails>> Login(string email, string password)
        {
            ResponseWrapper<UserDetails> result = await _database.LoginAsync(email, password, GetClientIpAddress(), false);
            // At this point we know what the UserId is, so we should set the SignalR user id to be the same
            if (result.IsSuccess) 
            {
                await SetSignalRGroupId(result.Data.UserId);
            }

            return result;
        }
        public async Task<ResponseWrapper<ConnectResult>> Connect(string loginToken)
        {
            try
            {
                // Directly fetching the login result from the stored procedure
                ConnectResult result = new ConnectResult();
                result.PhotosBaseUrl = Environment.GetEnvironmentVariable("COMPANIONATION_PHOTO_BASE_URL") ?? string.Empty;

                ResponseWrapper<UserDetails> userDetails = null;
                if (!string.IsNullOrWhiteSpace(loginToken))
                {
                    userDetails = await _database.GetUserAsync(loginToken);
                    // At this point we know what the UserId is, so we should set the SignalR user id to be the same
                    if (userDetails.IsSuccess)
                    {
                        await SetSignalRGroupId(userDetails.Data.UserId);
                    }
                }
                result.CurrentUser = userDetails;
                return ResponseWrapper<ConnectResult>.Success(result);
            } catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in Connect method.");
                return ResponseWrapper<ConnectResult>.Fail(50000, "Unknown error occurred while connecting");
            }
        }

        public async Task<string> GetCurrentVersion()
        {
            return Util.GetCurrentVersion();
        }

        public async Task LogError(DateTime timestamp, string message, string version)
        {
            ErrorLog.LogError(timestamp, "CLIENT: " + message, version);
        }

        public async Task<ResponseWrapper<List<Companion>>> GetContestLeaderBoard()
        {
            return await _database.GetContestLeaderBoard();
        }

        public async Task<ResponseWrapper<CompanioNitaAdvice>> GetCompanioNitaAdviceById(int adviceId)
        {
            return await _database.GetCompanitaAdvice(adviceId);
        }
        public async Task<ResponseWrapper<List<CompanioNitaAdvice>>> GetCompanioNitaAdvice(int start, int count)
        {
            return await _database.GetCompanitaAdvice(start, count);
        }

        public async Task<ResponseWrapper<string>> AskCompanioNita(string loginToken, string message)
        {
            // Direct validation is handled within stored procedures
            ResponseWrapper<UserDetails> result = await _database.GetUserAsync(loginToken);
            if (!result.IsSuccess) 
            {
                return ResponseWrapper<string>.Fail(result.ErrorCode, result.Message);
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                message = "Please provide me with some creative advice of your choosing.";
            }

            string answer = await _companioNita.AskCompanioNitaAsync(message);

            if (!string.IsNullOrWhiteSpace(answer))
            {
                ResponseWrapper<bool> response = await _database.SaveAdvice(loginToken, message, answer);
                if (!response.IsSuccess) return ResponseWrapper<string>.Fail(response.ErrorCode, response.Message);
            }
            return ResponseWrapper<string>.Success(answer);
        }

        public async Task<ResponseWrapper<List<Advice>>> GetAdvice(string loginToken)
        {
            return await _database.GetAdvice(loginToken);
        }

        public async Task<ResponseWrapper<int>> AskCompanioNitaAboutConversation(string loginToken, int userId)
        {
            // Get conversation
            ResponseWrapper<List<UserMessage>> messages = await _database.GetMessagesWithUserAsync(loginToken, userId);
            if (!messages.IsSuccess) return ResponseWrapper<int>.Fail(messages.ErrorCode, messages.Message);

            string messageText;
            if (messages.Data.Any())
            {
                messageText = await _companioNita.AskCompanioNitaAboutConversation(messages.Data);
            }
            else
            {
                // This is the first message, so introduce the people
                ResponseWrapper<UserDetails> user1 = await _database.GetUserAsync(loginToken);
                if (!user1.IsSuccess) return ResponseWrapper<int>.Fail(user1.ErrorCode, user1.Message);
                
                ResponseWrapper<UserConversation> user2 = await _database.StartUserConversationAsync(loginToken, userId);
                if (!user2.IsSuccess) return ResponseWrapper<int>.Fail(user2.ErrorCode, user2.Message);
                
                messageText = await _companioNita.AskCompanioNitaToIntroduce(user1.Data, user2.Data);
            }

            if (string.IsNullOrWhiteSpace(messageText)) return ResponseWrapper<int>.Fail(200000, "CompanioNita is busy");

            // Send the message to the specified user
            ResponseWrapper<SendMessageResult> result = await _database.SendMessageAsync(loginToken, userId, messageText, true);
            if (!result.IsSuccess) return ResponseWrapper<int>.Fail(result.ErrorCode, result.Message);
            if (result.Data.IgnoredSince == null)
            {
                // Send a PUSH notification to the client about a new message waiting
                await PushNotification(result.Data);
            }
            return ResponseWrapper<int>.Success(result.Data.MessageId);
        }

        public async Task<ResponseWrapper<bool>> AddIgnore(string loginToken, int userId)
        {
            return await _database.AddIgnore(loginToken, userId);
        }
        public async Task<ResponseWrapper<bool>> RemoveIgnore(string loginToken, int userId)
        {
            return await _database.RemoveIgnore(loginToken, userId);
        }

        public async Task<ResponseWrapper<bool>> GuaranteeConfirm(string verificationCode)
        {
            try
            {
                return await _database.GuaranteeConfirm(verificationCode);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GuaranteeConfirm method.");
                return ResponseWrapper<bool>.Fail(50000, "An unexpected error occurred while confirming the guarantee.");
            }
        }
        public async Task<ResponseWrapper<object>> GuaranteeEmail(string loginToken, string email)
        {
            try
            {
                // Validate the logintoken first, so that we don't waste a call to the openAI API if the user isn't logged in
                ResponseWrapper<UserDetails> currentUser = await _database.GetUserAsync(loginToken);
                if (!currentUser.IsSuccess) return ResponseWrapper<object>.Fail(currentUser.ErrorCode, currentUser.Message);

                ResponseWrapper<string> verificationCode = await _database.GuaranteeEmailAsync(loginToken, email);
                if (verificationCode.IsSuccess)
                {
                    if (verificationCode.Data != null)  // returns null if the connection already exists
                    {
                        // Send an email to the user to confirm that they actually know the person who is doing the guarantee
                        await SendConfirmationEmailAsync(email, verificationCode.Data, currentUser);
                    }

                    return ResponseWrapper<object>.Success(null);
                }
                else
                {
                    return ResponseWrapper<object>.Fail(verificationCode.ErrorCode, verificationCode.Message);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GuaranteeEmail method.");
                return ResponseWrapper<object>.Fail(50000, "An unexpected error occurred while sending the guarantee email.");
            }
        }


        public async Task<ResponseWrapper<object>> GuaranteeUser(string loginToken, string email, byte[] imageData)
        {
            try
            {
                // Validate the logintoken first, so that we don't waste a call to the openAI API if the user isn't logged in
                ResponseWrapper<UserDetails> currentUser = await _database.GetUserAsync(loginToken);
                if (!currentUser.IsSuccess) return ResponseWrapper<object>.Fail(currentUser.ErrorCode, currentUser.Message);

                // I probably don't want to do this in the long run
                ErrorLog.LogInfo("Detecting Face for userid = " + currentUser.Data.UserId + "(" + currentUser.Data.Email + "), trying to guarantee email = " + email);

                // Contact OpenAI to make sure the image contains a person's face
                if (!await _companioNita.DetectFaceAsync(imageData))
                    return ResponseWrapper<object>.Fail(200001, "No Face Detected");

                // The stored procedure will validate the token and perform the operation
                ResponseWrapper<string> verificationCode = await _database.GuaranteeUserAsync(loginToken, email, imageData);
                if (verificationCode.IsSuccess)
                {
                    if (verificationCode.Data != null)
                    {
                        // Send an email to the user to confirm that they actually know the person who is doing the guarantee
                        await SendConfirmationEmailAsync(email, verificationCode.Data, currentUser, imageData);
                        // OLD METHOD //await SendWelcomeEmailAsync(email, verificationCode.Data);
                    }
                    return ResponseWrapper<object>.Success(null);
                }
                else
                {
                    return ResponseWrapper<object>.Fail(verificationCode.ErrorCode, verificationCode.Message);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GuaranteeUser method.");
                return ResponseWrapper<object>.Fail(50000, "An unexpected error occurred while guaranteeing the user.");
            }
        }

        public async Task<ResponseWrapper<Guid>> UploadPhoto(string loginToken, byte[] imageData)
        {
            // Validate the logintoken first, so that we don't waste a call to the openAI API if the user isn't logged in
            ResponseWrapper<UserDetails> currentUser = await _database.GetUserAsync(loginToken);
            if (!currentUser.IsSuccess) return ResponseWrapper<Guid>.Fail(currentUser.ErrorCode, currentUser.Message);

            // Check to make sure the photo aspect ratio is within bounds
            using (var ms = new MemoryStream(imageData))
            {
                using (var image = Image.FromStream(ms))
                {
                    double aspectRatio = (double)image.Width / image.Height;
                    if (aspectRatio < 0.4 || aspectRatio > 2.5)
                    {
                        return ResponseWrapper<Guid>.Fail(200002, "Invalid aspect ratio. The aspect ratio must be between 0.5 and 2.0.");
                    }

                    // Check the file size and pixel count
                    if (imageData.Length > 2 * 1024 * 1024) // 2MB
                    {
                        return ResponseWrapper<Guid>.Fail(200003, "File size exceeds the limit.");
                    }

                    if (image.Width * image.Height > 1500000) // 1.5MP
                    {
                        return ResponseWrapper<Guid>.Fail(200004, "Pixel count exceeds the limit.");
                    }

                    // Check the image format
                    if (!image.RawFormat.Equals(ImageFormat.Jpeg))
                    {
                        return ResponseWrapper<Guid>.Fail(200005, "Invalid image format. Only JPEG images are allowed.");
                    }
                }
            }

            // Contact OpenAI to make sure the image contains a person's face
            if (!await _companioNita.DetectFaceAsync(imageData))
                return ResponseWrapper<Guid>.Fail(200001, "No Face Detected");

            // Validate the login token and upload the image to the database
            return await _database.UploadPhotoAsync(loginToken, imageData, GetClientIpAddress());
        }


        // Method to fetch users guaranteed by the logged-in user
        public async Task<ResponseWrapper<List<GuaranteedUser>>> GetGuaranteedUsers(string loginToken)
        {
            // Fetch the list of guaranteed users from the database
            return await _database.GetGuaranteedUsersAsync(loginToken);
        }



        public async Task<ResponseWrapper<UserConversation>> StartUserConversation(string loginToken, int userId)
        {
            // Fetch user details from the database
            return await _database.StartUserConversationAsync(loginToken, userId);
        }

        public async Task<ResponseWrapper<bool>> RemoveGuarantee(string loginToken, int imageId)
        {
            try
            {
                // Call the database method to remove the guarantee using the ImageID
                var result = await _database.RemoveGuaranteeAsync(loginToken, imageId);

                if (!result.IsSuccess)
                {
                    // Log specific error code and message from the ResponseWrapper
                    ErrorLog.LogErrorMessage($"Error {result.ErrorCode}: {result.Message}");
                }

                return result; // Return the ResponseWrapper directly from the database method
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in RemoveGuarantee method.");
                return ResponseWrapper<bool>.Fail(50000, "An unexpected error occurred while removing the guarantee.");
            }
        }


        public async Task<bool> UpdateReview(string loginToken, int imageId, int rating, string review)
        {
            // To be implemented; include token validation in stored procedure
            return false;
        }

        public async Task<ResponseWrapper<object>> CheckVerificationCode(string verificationCode)
        {
            return await _database.CheckVerificationCode(verificationCode);
        }
        public async Task<ResponseWrapper<object>> ResetPassword(string verificationCode, string newPassword)
        {
            return await _database.ResetPasswordAsync(verificationCode, newPassword);
        }

        // TODO - remove this!!!
        public async Task NotifyUpdateAvailable()
        {
            //await Clients.All.SendAsync("ReceiveUpdateNotification");
        }

        private string LoadEmailTemplate(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error loading email template.");
                return null;
            }
        }

        private async Task SendWelcomeEmailAsync(string email, string verificationCode)
        {
            string textTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.WelcomeEmail.txt");
            string htmlTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.WelcomeEmail.html");

            textTemplate = textTemplate.Replace("{Email}", email);
            textTemplate = textTemplate.Replace("{VerificationCode}", verificationCode);

            htmlTemplate = htmlTemplate.Replace("{Email}", email);
            htmlTemplate = htmlTemplate.Replace("{VerificationCode}", verificationCode);

            await Email.SendEmailAsync("Info@companionation.com", email, "Welcome to CompanioNation™!", textTemplate, htmlTemplate);
        }
        private async Task SendResetPasswordEmail(string email, string verificationCode)
        {
            string textTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ResetPasswordEmail.txt");
            string htmlTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ResetPasswordEmail.html");

            // Replace placeholders with the verification code
            textTemplate = textTemplate.Replace("{Email}", email);
            textTemplate = textTemplate.Replace("{VerificationCode}", verificationCode);
            htmlTemplate = htmlTemplate.Replace("{Email}", email);
            htmlTemplate = htmlTemplate.Replace("{VerificationCode}", verificationCode);

            // Send the email without confirming whether the email address exists
            await Email.SendEmailAsync("Info@companionation.com", email, "Reset Password Request", textTemplate, htmlTemplate);
        }
        private async Task SendConfirmationEmailAsync(string email, string verificationCode, ResponseWrapper<UserDetails> currentUser)
        {
            string textTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ConfirmationEmail.txt");
            string htmlTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ConfirmationEmail.html");

            textTemplate = textTemplate.Replace("{Email}", email);
            textTemplate = textTemplate.Replace("{RequestorEmail}", currentUser.Data.Email);
            textTemplate = textTemplate.Replace("{VerificationCode}", verificationCode);

            htmlTemplate = htmlTemplate.Replace("{Email}", email);
            htmlTemplate = htmlTemplate.Replace("{RequestorEmail}", currentUser.Data.Email);
            htmlTemplate = htmlTemplate.Replace("{VerificationCode}", verificationCode);

            await Email.SendEmailAsync("Info@companionation.com", email, "Confirmation Email", textTemplate, htmlTemplate);
        }

        private async Task SendConfirmationEmailAsync(string email, string verificationCode, ResponseWrapper<UserDetails> currentUser, byte[] imageData)
        {
            string textTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ConfirmationEmailWithImage.txt");
            string htmlTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ConfirmationEmailWithImage.html");

            textTemplate = textTemplate.Replace("{Email}", email);
            textTemplate = textTemplate.Replace("{RequestorEmail}", currentUser.Data.Email);
            textTemplate = textTemplate.Replace("{VerificationCode}", verificationCode);

            htmlTemplate = htmlTemplate.Replace("{Email}", email);
            htmlTemplate = htmlTemplate.Replace("{RequestorEmail}", currentUser.Data.Email);
            htmlTemplate = htmlTemplate.Replace("{VerificationCode}", verificationCode);

            string imageBase64 = Convert.ToBase64String(imageData);
            htmlTemplate = htmlTemplate.Replace("{Image}", $"<img src='data:image/png;base64,{imageBase64}' alt='User Image' />");

            await Email.SendEmailAsync("Info@companionation.com", email, "Confirmation Email with Image", textTemplate, htmlTemplate);
        }


        public async Task<ResponseWrapper<Settings>> GetSettings()
        {
            Settings settings = await _database.GetAllSettingsAsync();
            if (settings == null) return ResponseWrapper<Settings>.Fail(50000, "Error getting settings.");

            // Censor certain privileged system settings
            settings.LastMaintenanceRun = DateTime.Now;
            settings.PreviousDailyAdvice = null;

            return ResponseWrapper<Settings>.Success(settings);
        }

        public async Task<ResponseWrapper<List<UserImage>>> GetUserImages(string loginToken)
        {
            // Validation handled within the stored procedure
            return await _database.GetUserImagesAsync(loginToken);
        }


        public async Task<ResponseWrapper<List<UserConversation>>> GetUserConversations(string loginToken)
        {
            // Validate login token and fetch user conversations from the database
            return await _database.GetUserConversationsAsync(loginToken);
        }

        public async Task<ResponseWrapper<List<UserMessage>>> GetMessagesWithUser(string loginToken, int userId)
        {
            // Validate login token and fetch message history with the specified user
            return await _database.GetMessagesWithUserAsync(loginToken, userId);
        }

        public async Task<ResponseWrapper<int>> SendMessage(string loginToken, int userId, string messageText)
        {
            if (string.IsNullOrWhiteSpace(messageText)) return ResponseWrapper<int>.Fail(50000, "message is blank");
            // Limit the size of a message users may send
            if (messageText.Length > 1024) messageText = messageText.Substring(0, 1024);

            // Validate login token and send the message to the specified user
            ResponseWrapper<SendMessageResult> result = await _database.SendMessageAsync(loginToken, userId, messageText);
            if (!result.IsSuccess) return ResponseWrapper<int>.Fail(result.ErrorCode, result.Message);
            if (result.Data.IgnoredSince == null)
            {
                await PushNotification(result.Data);
            }
            return ResponseWrapper<int>.Success(result.Data.MessageId);
        }
        private async Task PushNotification(SendMessageResult parameters)
        {
            // Send a PUSH notification asynchronously to the client about a new message waiting
            //Clients.Groups(parameters.ToUserId.ToString()).SendAsync("ReceiveMessage", Util.StripHtmlTags(parameters.MessageText), parameters.FromUserId, parameters.FromUserName);

            // TODO - use new method with the PUSH API
            // Send Web Push notification if the push token is available
            if (!string.IsNullOrEmpty(parameters.PushToken))
            {
                var pushService = new PushService();
                bool success = await pushService.SendAsync(parameters.PushToken, parameters);
                if (!success)
                {
                    // SOMETHING WENT WRONG WITH THE PUSH NOTIFICATION, SO DELETE THE PUSH TOKEN FROM THE DATABASE
                    await _database.UpdatePushTokenAsync(parameters.LoginToken, "");
                    
                    // WE CAN'T DO THE BELOW BECAUSE OF BROWSER SECURITY ISSUES!!
                    // Something went haywire with the PUSH API, so clear the database and
                    // notify the other end via SignalR that it needs to re-register
                    //Clients.Groups(parameters.ToUserId.ToString()).SendAsync("RenewPushRegistration");
                }
            }
        }

        public async Task<ResponseWrapper<bool>> UpdatePushToken(string loginToken, string pushToken)
        {
            return await _database.UpdatePushTokenAsync(loginToken, pushToken);
        }

        public async Task<ResponseWrapper<List<UserMessage>>> GetIgnoredMessages(string loginToken)
        {
            // Validation handled within the stored procedure
            return await _database.GetIgnoredMessagesAsync(loginToken);
        }

        public async Task<ResponseWrapper<List<Companion>>> FindCompanions(string loginToken, bool cisMale, bool cisFemale, bool other, bool transMale, bool transFemale, List<int> cities, int ageMin, int ageMax, bool showIgnoredUsers)
        {
            ResponseWrapper<List<Companion>> result = await _database.FindCompanionsAsync(loginToken, cisMale, cisFemale, other, transMale, transFemale, cities, ageMin, ageMax, showIgnoredUsers);
            return result;
        }


        public async Task RequestNewVerificationCode(string email)
        {
            try
            {
                // Attempt to generate a new verification code and send it to the user
                string verificationCode = await _database.GenerateNewVerificationCodeAsync(email);

                if (string.IsNullOrWhiteSpace(verificationCode)) return;

                // Send the verification email without revealing if the email exists in the database
                await SendResetPasswordEmail(email, verificationCode);
            }
            catch (Exception ex)
            {
                // Log the error but do not expose any details to the caller
                ErrorLog.LogErrorException(ex, "RequestNewVerificationCode");
            }
        }


        public async Task<ResponseWrapper<bool>> UpdateUserDetails(string loginToken, UserDetails userDetails)
        {
            // Validate the token and update user info using the stored procedure
            return await _database.UpdateUserDetailsAsync(loginToken, userDetails);
        }



        // Method to update the visibility of an image
        public async Task<ResponseWrapper<bool>> UpdateImageVisibility(string loginToken, int imageId, bool isVisible)
        {
            // Call the database method to update the image visibility
            ResponseWrapper<bool> result = await _database.UpdateImageVisibilityAsync(loginToken, imageId, isVisible);
            return result;
        }

        public async Task<ResponseWrapper<bool>> UpdateReviewVisibility(string loginToken, int imageId, bool isPublic)
        {
            return await _database.UpdateReviewVisibilityAsync(loginToken, imageId, isPublic);
        }


        public async Task<ResponseWrapper<bool>> UpdateImageReview(string loginToken, int imageId, int rating, string review)
        {
            return await _database.UpdateImageReviewAsync(loginToken, imageId, rating, review);
        }


        public async Task<ResponseWrapper<string>> TriggerMaintenanceManually(string loginToken)
        {
            // Validate the login token and check if the user is an administrator
            ResponseWrapper<UserDetails> result = await _database.GetUserAsync(loginToken);

            if (!result.IsSuccess) return ResponseWrapper<string>.Fail(result.ErrorCode, result.Message);
            if (!result.Data.IsAdministrator)
            {
                ErrorLog.LogErrorMessage($"SECURITY BREACH: Unauthorized access attempt to TriggerMaintenanceManually() by User ID: {result?.Data.UserId}, IP Address: {GetClientIpAddress()}");

                // Return an unauthorized message if the user is not an admin
                return ResponseWrapper<string>.Fail(100009, "Unauthorized access. Only administrators can perform this action.");
            }

            try
            {
                // Run the maintenance task
                await _maintenanceEventService.RunDailyMaintenanceAsync(CancellationToken.None); // Make sure RunDailyMaintenanceAsync is public
                return ResponseWrapper<string>.Success("Daily maintenance executed successfully.");
            }
            catch (Exception ex)
            {
                // Log the error and return a failure message
                ErrorLog.LogErrorException(ex, "Error executing daily maintenance manually.");
                return ResponseWrapper<string>.Fail(ex.HResult, "An error occurred while executing maintenance.");
            }
        }


        public async Task<ResponseWrapper<List<Country>>> GetCountries(string continent)
        {
            try
            {
                // Fetch the list of countries from the database based on the continent
                ResponseWrapper<List<Country>> result = await _database.GetCountriesAsync(continent);
                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GetCountries method.");
                return ResponseWrapper<List<Country>>.Fail(50000, "An unexpected error occurred while fetching countries.");
            }
        }

        public async Task<ResponseWrapper<List<City>>> GetNearbyCities(string loginToken)
        {
            try
            {
                // Validate the login token
                ResponseWrapper<UserDetails> user = await _database.GetUserAsync(loginToken);
                if (!user.IsSuccess) return ResponseWrapper<List<City>>.Fail(user.ErrorCode, user.Message);

                // Fetch the list of searchable cities from the database
                ResponseWrapper<List<City>> result = await _database.GetNearbyCitiesAsync(loginToken);
                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GetNearbyCities method.");
                return ResponseWrapper<List<City>>.Fail(50000, "An unexpected error occurred while fetching searchable cities.");
            }
        }
        public async Task<ResponseWrapper<List<City>>> GetCities(string country, string searchTerm)
        {
            try
            {
                // Fetch the list of cities from the database based on the continent, country, and search term
                ResponseWrapper<List<City>> result = await _database.GetCitiesAsync(country, searchTerm);
                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GetCities method.");
                return ResponseWrapper<List<City>>.Fail(50000, "An unexpected error occurred while fetching cities.");
            }
        }
        public async Task<ResponseWrapper<City>> GetCity(string loginToken, int geonameid)
        {
            try
            {
                // Fetch the city details from the database based on the geonameid
                ResponseWrapper<City> result = await _database.GetCityAsync(loginToken, geonameid);
                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GetCity method.");
                return ResponseWrapper<City>.Fail(50000, "An unexpected error occurred while fetching city details.");
            }
        }

        // Returns (emailExists, oauthRequired)
        public async Task<ResponseWrapper<CheckEmailResult>> CheckEmailExists(string email)
        {
            try
            {
                CheckEmailResult result = await _database.CheckEmailExistsAsync(email);
                return ResponseWrapper<CheckEmailResult>.Success(result);
            }
            catch (Exception ex)
            {
                return ResponseWrapper<CheckEmailResult>.Fail(ex.HResult, ex.Message);
            }
        }

        // TODO - check the IP address of the creator and make sure it isn't a DOS attack
        public async Task<ResponseWrapper<bool>> CreateNewUser(string email, string password)
        {
            try
            {
                email = email.Trim();
                string validation_code = await _database.CreateNewUserAsync(email, password, GetClientIpAddress());
                if (string.IsNullOrWhiteSpace(validation_code)) return ResponseWrapper<bool>.Fail(50000, "Could not create new user");
                await SendWelcomeEmailAsync(email, validation_code);

                return ResponseWrapper<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return ResponseWrapper<bool>.Fail(ex.HResult, ex.Message);
            }
        }


#if DEBUG
        // TEST SUITE CODE, ONLY IN DEBUG VERSION, NOT FOR PRODUCTION
        public async Task<ResponseWrapper<string>> RunTestSuite(string loginToken)
        {
            // Validate the login token and check if the user is an administrator
            ResponseWrapper<UserDetails> user = await _database.GetUserAsync(loginToken);

            if (!user.IsSuccess || !user.Data.IsAdministrator)
            {
                return ResponseWrapper<string>.Fail(user.ErrorCode, user.Message);
            }
            
            string result = "";
            // Add any specific test implementations here
            result += ",";

            bool success = await Email.SendEmailAsync("Info@companionation.com", "errors@companionation.com", "Welcome to CompanioNation™", "email sending test", "html body");
            result += success;

            return ResponseWrapper<string>.Success(result);
        }

        public byte[] GeneratePngImage(int width, int height, Color backgroundColor)
        {
            using (Bitmap bitmap = new Bitmap(width, height))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(backgroundColor);
                    string text = "Hello, World!";
                    using (Font font = new Font("Arial", 20))
                    {
                        using (Brush brush = new SolidBrush(Color.Black))
                        {
                            graphics.DrawString(text, font, brush, new PointF(10, 10));
                        }
                    }
                }
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    bitmap.Save(memoryStream, ImageFormat.Png);
                    return memoryStream.ToArray();
                }
            }
        }
#endif

        public async Task ReceiveFeedback(string feedbackText)
        {
            try
            {
                // Log the feedback or process it as needed
                Console.WriteLine($"Received feedback: {feedbackText}");

                // Optionally, save the feedback to the database
                //await _database.SaveFeedbackAsync(feedbackText);
                await Email.SendEmailAsync("Info@companionation.com", "feedback@companionation.com", "CompanioNation™ Feedback", feedbackText, feedbackText);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in ReceiveFeedback method.");
            }
        }

        public async Task<ResponseWrapper<UserDetails>> LoginWithGoogle(string code, string code_verifier, string redirect_uri)
        {
            try
            {
                // Validate the Google ID token and retrieve user details
                ResponseWrapper<UserDetails> result = await _database.LoginWithGoogleAsync(code, code_verifier, redirect_uri, GetClientIpAddress());

                if (result.IsSuccess)
                {
                    // Set the SignalR group ID for the user
                    await SetSignalRGroupId(result.Data.UserId);
                }

                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in LoginWithGoogle method.");
                return ResponseWrapper<UserDetails>.Fail(50000, "An unexpected error occurred while logging in with Google.");
            }
        }
    }
}
