using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace AuthService
{
    public static class GoogleAuthService
    {
        private static readonly string[] Scopes = { DriveService.Scope.Drive };
        private const string ApplicationName = "GDrive CLI Manager";
        private const string TokenStorePath = ".gdrive-tokens";

        public static async Task<DriveService> AuthenticateAsync(string credentialsPath)
        {
            if (!File.Exists(credentialsPath))
            {
                throw new FileNotFoundException(
                    $"client_secret.json not found at: {Path.GetFullPath(credentialsPath)}\n" +
                    "Please follow the README instructions to place your credentials file.");
            }

            UserCredential credential;

            await using var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read);

            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore(TokenStorePath, true));

            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName
            });
        }
    }
}
