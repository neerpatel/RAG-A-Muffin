using System.IO;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Drive.v3;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace RagAMuffin.Auth
{
    public static class GoogleAuth
    {
        private const string TokenStoreFolder = "/app/data/tokens";
        private const string CredentialsPath  = "/app/data/credentials.json";

        private static async Task<GoogleAuthorizationCodeFlow> CreateFlowAsync()
        {
            var credentialsPath = File.Exists(CredentialsPath)
                ? CredentialsPath
                : Path.Combine(AppContext.BaseDirectory, "credentials.json");
            await using var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read);
            var secrets = GoogleClientSecrets.FromStream(stream).Secrets;

            Directory.CreateDirectory(TokenStoreFolder);

            return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = secrets,
                Scopes = new[]
                {
                    GmailService.Scope.GmailReadonly,
                    DriveService.Scope.DriveReadonly,
                    CalendarService.Scope.CalendarReadonly
                },
                DataStore = new FileDataStore(TokenStoreFolder, fullPath: true)
            });
        }

        public static async Task<bool> HasStoredCredentialAsync(string userId)
        {
            try
            {
                var flow = await CreateFlowAsync();
                return await flow.LoadTokenAsync(userId, CancellationToken.None) != null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static async Task<string> GetAuthorizationUrlAsync(string userId, string redirectUri)
        {
            var flow = await CreateFlowAsync();
            var request = (global::Google.Apis.Auth.OAuth2.Requests.GoogleAuthorizationCodeRequestUrl)flow.CreateAuthorizationCodeRequest(redirectUri);
            request.AccessType = "offline";
            request.Prompt = "consent";
            request.IncludeGrantedScopes = "true";
            request.State = Uri.EscapeDataString(userId);
            return request.Build().AbsoluteUri;
        }

        public static async Task<TokenResponse> ExchangeCodeForTokenAsync(string userId, string code, string redirectUri)
        {
            var flow = await CreateFlowAsync();
            return await flow.ExchangeCodeForTokenAsync(userId, code, redirectUri, CancellationToken.None);
        }

        public static async Task<GmailService> CreateGmailServiceAsync(string userId)
        {
            var credential = await GetCredentialAsync(userId);
            return new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "RAG-A-Muffin"
            });
        }

        public static async Task<DriveService> CreateDriveServiceAsync(string userId)
        {
            var credential = await GetCredentialAsync(userId);
            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "RAG-A-Muffin"
            });
        }

        public static async Task<CalendarService> CreateCalendarServiceAsync(string userId)
        {
            var credential = await GetCredentialAsync(userId);
            return new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "RAG-A-Muffin"
            });
        }

        private static async Task<UserCredential> GetCredentialAsync(string userId)
        {
            var flow = await CreateFlowAsync();
            var token = await flow.LoadTokenAsync(userId, CancellationToken.None);
            if (token is null)
                throw new InvalidOperationException($"No stored token for user '{userId}'.");
            return new UserCredential(flow, userId, token);
        }
    }
}
