using Microsoft.Data.SqlClient;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;
using Dental_Clinic.Models;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace Dental_Clinic.Services
{
    public class GoogleAuthService
    {
        private readonly DatabaseService _databaseService;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private bool _configValidated = false;
        private bool _isConfigValid = false;

        // Load from configuration instead of hardcoding
        private string GoogleClientId => _configuration["GoogleOAuth:ClientId"] ?? "";
        private string GoogleClientSecret => _configuration["GoogleOAuth:ClientSecret"] ?? "";
        private string RedirectUri => _configuration["GoogleOAuth:RedirectUri"] ?? "http://127.0.0.1:5000/";

        public GoogleAuthService(DatabaseService databaseService, HttpClient httpClient, IConfiguration configuration)
        {
            _databaseService = databaseService;
            _httpClient = httpClient;
            _configuration = configuration;

            // Validate config early but don't throw - just log
            try
            {
                _isConfigValid = ValidateConfiguration();
                if (_isConfigValid)
                {
                    Debug.WriteLine($"[GoogleAuthService] Loaded GoogleOAuth ClientId: {MaskClientId(GoogleClientId)} RedirectUri: {RedirectUri}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GoogleAuthService] Configuration error: {ex.Message}");
                _isConfigValid = false;
            }
            finally
            {
                _configValidated = true;
            }
        }

        private bool ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(GoogleClientId) || GoogleClientId.Contains("YOUR_") || GoogleClientId.Contains("@"))
            {
                Debug.WriteLine("[GoogleAuthService] Google ClientId looks invalid. Google OAuth features will be disabled.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(GoogleClientSecret) || GoogleClientSecret.Length < 10)
            {
                Debug.WriteLine("[GoogleAuthService] Google ClientSecret missing or too short. Google OAuth features will be disabled.");
                return false;
            }
            if (!RedirectUri.StartsWith("http://127.0.0.1") && !RedirectUri.StartsWith("http://localhost"))
            {
                Debug.WriteLine("[GoogleAuthService] Warning: RedirectUri is not a localhost/loopback URL. Ensure it matches the one registered in Google Console.");
            }
            return true;
        }

        private void EnsureConfigured()
        {
            if (!_configValidated || !_isConfigValid)
            {
                throw new InvalidOperationException("Google OAuth is not configured. Please add valid GoogleOAuth settings to appsettings.json or appsettings.Development.json");
            }
        }

        private string MaskClientId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "(empty)";
            if (id.Length <= 8) return new string('*', id.Length);
            return id.Substring(0, 4) + new string('*', Math.Max(0, id.Length - 8)) + id.Substring(id.Length - 4);
        }

        public string GetGoogleAuthUrl()
        {
            EnsureConfigured();
            var scope = Uri.EscapeDataString("openid profile email");
            var state = GenerateState();
            var url = "https://accounts.google.com/o/oauth2/v2/auth" +
                      $"?client_id={GoogleClientId}" +
                      $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                      "&response_type=code" +
                      $"&scope={scope}" +
                      $"&state={state}" +
                      "&prompt=select_account";
            Debug.WriteLine($"[GoogleOAuth] Generated auth URL with state={state.Substring(0, 10)}...");
            return url;
        }

        public async Task<UserSession?> AuthenticateWithGoogleAsync(string code, string state)
        {
            EnsureConfigured();
            Debug.WriteLine($"[GoogleOAuth] AuthenticateWithGoogleAsync called with code={code.Substring(0, 20)}..., state={state.Substring(0, 10)}...");

            var tokenResponse = await ExchangeCodeForTokenAsync(code);
            if (tokenResponse == null)
            {
                Debug.WriteLine("[GoogleOAuth] Token exchange returned null!");
                return null;
            }

            Debug.WriteLine("[GoogleOAuth] Token received, fetching user info...");
            var userInfo = await GetGoogleUserInfoAsync(tokenResponse.AccessToken);
            if (userInfo == null || string.IsNullOrEmpty(userInfo.Email))
            {
                Debug.WriteLine("[GoogleOAuth] User info is null or missing email!");
                return null;
            }

            Debug.WriteLine($"[GoogleOAuth] Got user info for: {userInfo.Email}");
            var user = await GetOrCreateUserFromGoogleAsync(userInfo);
            if (user == null)
            {
                Debug.WriteLine("[GoogleOAuth] Failed to get/create user from DB!");
                return null;
            }

            Debug.WriteLine($"[GoogleOAuth] Successfully authenticated user {user.UserID}: {user.Email}");
            return new UserSession
            {
                UserId = user.UserID,
                UserName = $"{user.FirstName} {user.LastName}".Trim(),
                Role = user.RoleName,
                Token = GenerateToken(user),
                RoleSpecificId = user.PatientID
            };
        }

        private async Task<GoogleTokenResponse?> ExchangeCodeForTokenAsync(string code)
        {
            Debug.WriteLine("[GoogleOAuth] Exchanging code for token...");
            var tokenRequest = new Dictionary<string, string>
            {
          {"code", code},
    {"client_id", GoogleClientId},
        {"client_secret", GoogleClientSecret},
           {"redirect_uri", RedirectUri},
   {"grant_type", "authorization_code"}
    };
            var response = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(tokenRequest));
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[GoogleOAuth] Token exchange FAILED: {response.StatusCode} - {err}");
                return null;
            }
            var json = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"[GoogleOAuth] Token JSON: {json}");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var token = JsonSerializer.Deserialize<GoogleTokenResponse>(json, options);
            Debug.WriteLine($"[GoogleOAuth] Deserialized AccessToken length: {token?.AccessToken?.Length ?? 0}");
            return token;
        }

        private async Task<GoogleUserInfo?> GetGoogleUserInfoAsync(string accessToken)
        {
            Debug.WriteLine($"[GoogleOAuth] Fetching user info with token: {accessToken?.Substring(0, Math.Min(20, accessToken?.Length ?? 0))}...");
            var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            var resp = await _httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync();
                Debug.WriteLine($"[GoogleOAuth] User info fetch FAILED: {resp.StatusCode} - {errBody}");
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync();
            Debug.WriteLine($"[GoogleOAuth] User info JSON: {json}");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<GoogleUserInfo>(json, options);
        }

        private async Task<User?> GetOrCreateUserFromGoogleAsync(GoogleUserInfo g)
        {
            try
            {
                var useOnline = _databaseService.IsOnline;
                Debug.WriteLine($"[GoogleOAuth] Using {(useOnline ? "ONLINE" : "OFFLINE")} database for user {g.Email}");

                var cs = useOnline
                    ? "Server=db33114.public.databaseasp.net;Database=db33114;User Id=db33114;Password=T!t8?w5NdK-7;Encrypt=True;TrustServerCertificate=True;Connection Timeout=15;"
                    : "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=DentalClinicLocal;Integrated Security=True;Connect Timeout=30;Encrypt=False;";

                using var conn = new SqlConnection(cs);
                await conn.OpenAsync();
                Debug.WriteLine("[GoogleOAuth] DB connection opened");

                if (!useOnline)
                {
                    var ensure = @"IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Users') BEGIN
CREATE TABLE Users (UserID INT IDENTITY(1,1) PRIMARY KEY, RoleName NVARCHAR(50) NOT NULL, UserName NVARCHAR(100) NOT NULL UNIQUE, Password NVARCHAR(100) NOT NULL, FirstName NVARCHAR(100), LastName NVARCHAR(100), PhoneNumber NVARCHAR(50), Age INT, Sex NVARCHAR(20), Email NVARCHAR(100) NOT NULL);
CREATE TABLE Patient (PatientID INT IDENTITY(1,1) PRIMARY KEY, UserID INT NOT NULL, MedicalAlerts NVARCHAR(500), BirthDate DATE NOT NULL, Address NVARCHAR(500), MaritalStatus NVARCHAR(50), ProfileImg NVARCHAR(MAX), MedicalHistory NVARCHAR(MAX), PhoneNumber NVARCHAR(50), Email NVARCHAR(100), InsuranceProvider NVARCHAR(100), InsurancePolicyNumber NVARCHAR(100), CONSTRAINT FK_Patient_Users FOREIGN KEY (UserID) REFERENCES Users(UserID)); END";
                    using var cmdEnsure = new SqlCommand(ensure, conn);
                    await cmdEnsure.ExecuteNonQueryAsync();
                    Debug.WriteLine("[GoogleOAuth] Ensured tables exist");
                }

                using var cmdCheck = new SqlCommand("SELECT TOP 1 UserID, RoleName, UserName, FirstName, LastName, Email FROM Users WHERE Email=@Email", conn);
                cmdCheck.Parameters.AddWithValue("@Email", g.Email);

                using var r = await cmdCheck.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    Debug.WriteLine($"[GoogleOAuth] Found existing user: {g.Email}");
                    var existing = new User
                    {
                        UserID = r.GetInt32(0),
                        RoleName = r.GetString(1),
                        UserName = r.GetString(2),
                        FirstName = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                        LastName = r.IsDBNull(4) ? string.Empty : r.GetString(4),
                        Email = r.GetString(5)
                    };
                    r.Close();

                    using var pidCmd = new SqlCommand("SELECT PatientID FROM Patient WHERE UserID=@U", conn);
                    pidCmd.Parameters.AddWithValue("@U", existing.UserID);
                    var pid = await pidCmd.ExecuteScalarAsync();
                    if (pid != null) existing.PatientID = Convert.ToInt32(pid);
                    Debug.WriteLine($"[GoogleOAuth] Returning existing user ID={existing.UserID}");
                    return existing;
                }
                r.Close();

                Debug.WriteLine($"[GoogleOAuth] Creating NEW user for {g.Email}");
                var firstName = string.IsNullOrEmpty(g.GivenName) ? g.Email.Split('@')[0] : g.GivenName;
                var lastName = string.IsNullOrEmpty(g.FamilyName) ? "" : g.FamilyName;
                var userName = g.Email.Split('@')[0];

                using var cmdUser = new SqlCommand(@"INSERT INTO Users (RoleName,UserName,Password,FirstName,LastName,Email,PhoneNumber)
OUTPUT INSERTED.UserID VALUES (@Role,@UN,@PW,@FN,@LN,@EM,@PH)", conn);

                cmdUser.Parameters.AddWithValue("@Role", "Patient");
                cmdUser.Parameters.AddWithValue("@UN", userName);
                cmdUser.Parameters.AddWithValue("@PW", GenerateRandomPassword());
                cmdUser.Parameters.AddWithValue("@FN", firstName);
                cmdUser.Parameters.AddWithValue("@LN", lastName);
                cmdUser.Parameters.AddWithValue("@EM", g.Email);
                cmdUser.Parameters.AddWithValue("@PH", DBNull.Value);

                var newUserId = Convert.ToInt32(await cmdUser.ExecuteScalarAsync());
                Debug.WriteLine($"[GoogleOAuth] Created user ID={newUserId}");

                using var cmdPatient = new SqlCommand(@"INSERT INTO Patient (UserID, Email, BirthDate)
OUTPUT INSERTED.PatientID VALUES (@U,@E,@B)", conn);
                cmdPatient.Parameters.AddWithValue("@U", newUserId);
                cmdPatient.Parameters.AddWithValue("@E", g.Email);
                cmdPatient.Parameters.AddWithValue("@B", DateTime.UtcNow.AddYears(-25)); // Default age estimate

                var newPatientId = Convert.ToInt32(await cmdPatient.ExecuteScalarAsync());
                Debug.WriteLine($"[GoogleOAuth] Created patient ID={newPatientId}");

                return new User
                {
                    UserID = newUserId,
                    RoleName = "Patient",
                    UserName = userName,
                    FirstName = firstName,
                    LastName = lastName,
                    Email = g.Email,
                    PatientID = newPatientId
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GoogleOAuth] DB ERROR: {ex.Message}");
                Debug.WriteLine($"[GoogleOAuth] Stack: {ex.StackTrace}");
                return null;
            }
        }

        private string GenerateState()
        {
            var b = new byte[16];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(b);
            return Convert.ToBase64String(b).Replace("+", "-").Replace("/", "_").Replace("=", string.Empty);
        }
        private string GenerateRandomPassword()
        {
            var b = new byte[12];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(b);
            return Convert.ToBase64String(b);
        }
        private string GenerateToken(User u) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{u.UserID}:{u.Email}:{DateTime.UtcNow.Ticks}"));

        private class GoogleTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;

            [JsonPropertyName("token_type")]
            public string TokenType { get; set; } = string.Empty;

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; } = string.Empty;
        }

        private class GoogleUserInfo
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("email")]
            public string Email { get; set; } = string.Empty;

            [JsonPropertyName("verified_email")]
            public bool VerifiedEmail { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("given_name")]
            public string GivenName { get; set; } = string.Empty;

            [JsonPropertyName("family_name")]
            public string FamilyName { get; set; } = string.Empty;

            [JsonPropertyName("picture")]
            public string Picture { get; set; } = string.Empty;

            [JsonPropertyName("locale")]
            public string Locale { get; set; } = string.Empty;
        }
    }
}