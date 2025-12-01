using Microsoft.AspNetCore.Components;
using Dental_Clinic.Models;
using Dental_Clinic.Services;
using System.Diagnostics;

namespace Dental_Clinic.Components.Layout
{
    public partial class Login : IDisposable
    {
        private bool isLoginMode = true;
        private bool showLoginPassword = false;
        private bool showSignupPassword = false;
        private bool isLoading = false;
        private bool showSyncModal = false;
        private bool isAuthenticatingWithGoogle = false; // NEW: Track Google auth state

        private string errorMessage = string.Empty;
        private string successMessage = string.Empty;
        private string syncMessage = string.Empty;
        private int syncProgress = 0;

        private LoginModel loginModel = new LoginModel();
        private SignUpModel signUpModel = new SignUpModel();

        private string loginPasswordType => showLoginPassword ? "text" : "password";
        private string signupPasswordType => showSignupPassword ? "text" : "password";

        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private AuthService AuthService { get; set; } = default!;
        [Inject] private DatabaseService DatabaseService { get; set; } = default!;
        [Inject] private SyncService SyncService { get; set; } = default!;
        [Inject] private GoogleAuthService GoogleAuthService { get; set; } = default!;
        [Inject] private SessionService SessionService { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            try
            {
                var uri = Navigation.ToAbsoluteUri(Navigation.Uri);
                if (uri.AbsolutePath.Contains("/signup") || uri.AbsolutePath.Contains("/auth"))
                {
                    isLoginMode = uri.AbsolutePath.Contains("/login") || uri.AbsolutePath == "/";
                }

                if (DatabaseService != null)
                {
                    await DatabaseService.InitializeDatabaseAsync();
                }

                if (SyncService != null)
                {
                    SyncService.SyncProgressChanged += OnSyncProgressChanged;
                    SyncService.SyncCompleted += OnSyncCompleted;
                }

                if (DatabaseService != null && AuthService != null && !DatabaseService.IsOnline)
                {
                    await AuthService.SwitchToOfflineModeAsync();
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Initialization error: {ex.Message}";
                Console.WriteLine($"Login initialization error: {ex}");
            }
        }

        private void OnSyncProgressChanged(object? sender, SyncProgressEventArgs e)
        {
            syncMessage = e.Message;
            syncProgress = e.Percentage;
            InvokeAsync(StateHasChanged);
        }

        private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e)
        {
            syncMessage = e.Result.Message;
            showSyncModal = false;
            InvokeAsync(StateHasChanged);
        }

        private void SwitchToLogin()
        {
            isLoginMode = true;
            errorMessage = string.Empty;
            successMessage = string.Empty;
        }

        private void SwitchToSignUp()
        {
            isLoginMode = false;
            errorMessage = string.Empty;
            successMessage = string.Empty;
        }

        private void NavigateToForgotPassword()
        {
            Navigation.NavigateTo("/forgot-password");
        }

        private void ToggleLoginPassword() => showLoginPassword = !showLoginPassword;
        private void ToggleSignupPassword() => showSignupPassword = !showSignupPassword;
        private void ToggleTheme() { }

        private async Task ToggleOfflineMode()
        {
            try
            {
                if (AuthService == null) return;

                if (AuthService.IsOfflineMode)
                {
                    AuthService.SwitchToOnlineMode();
                    successMessage = "Switched to Online Mode";
                }
                else
                {
                    await AuthService.SwitchToOfflineModeAsync();
                    successMessage = "Switched to Offline Mode";
                }
                StateHasChanged();
            }
            catch (Exception ex)
            {
                errorMessage = $"Error toggling offline mode: {ex.Message}";
            }
        }

        private async Task StartFullSync()
        {
            try
            {
                if (SyncService == null) return;
                showSyncModal = true;
                syncMessage = "Starting sync...";
                syncProgress = 0;

                await SyncService.FullSyncDownloadAsync();
            }
            catch (Exception ex)
            {
                errorMessage = $"Sync error: {ex.Message}";
                showSyncModal = false;
            }
        }

        private async Task UploadPendingChanges()
        {
            if (!DatabaseService.IsOnline)
            {
                errorMessage = "Cannot upload: No internet connection";
                return;
            }
            showSyncModal = true;
            syncMessage = "Uploading pending changes...";
            syncProgress = 0;

            await SyncService.SyncUploadPendingChangesAsync();
        }

        private async Task HandleLogin()
        {
            isLoading = true;
            errorMessage = string.Empty;
            successMessage = string.Empty;

            try
            {
                if (AuthService == null)
                {
                    errorMessage = "Authentication service not available";
                    return;
                }

                var session = await AuthService.LoginAsync(loginModel);

                if (session != null)
                {
                    // Save session
                    await SessionService.SaveSessionAsync(session);
                    
                    // Navigate based on role
                    string dashboardRoute = GetDashboardRoute(session.Role);
                    Navigation.NavigateTo(dashboardRoute, forceLoad: true);
                }
                else
                {
                    errorMessage = "Invalid credentials. Please try again.";
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Login failed: {ex.Message}";
                Console.WriteLine($"Login error: {ex}");
            }
            finally
            {
                isLoading = false;
            }
        }

        private async Task HandleSignUp()
        {
            isLoading = true;
            errorMessage = string.Empty;
            successMessage = string.Empty;

            try
            {
                if (AuthService == null)
                {
                    errorMessage = "Authentication service not available";
                    return;
                }

                var result = await AuthService.RegisterAsync(signUpModel);

                if (result.Success)
                {
                    successMessage = result.Message;

                    // Auto-login after successful registration
                    await Task.Delay(2000); // Show success message for 2 seconds

                    loginModel.EmailOrUsername = signUpModel.Email;
                    loginModel.Password = signUpModel.Password;
                    await HandleLogin();
                }
                else
                {
                    errorMessage = result.Message;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Registration failed: {ex.Message}";
                Console.WriteLine($"Registration error: {ex}");
            }
            finally
            {
                isLoading = false;
            }
        }

        private async Task HandleGoogleAuth()
        {
            if (isAuthenticatingWithGoogle)
            {
                Debug.WriteLine("[Login] Already authenticating, blocked duplicate");
                return;
            }

            OAuthCallbackListener? listener = null;

            try
            {
                if (GoogleAuthService == null)
                {
                    errorMessage = "Google auth unavailable";
                    Debug.WriteLine("[Login] GoogleAuthService is NULL!");
                    return;
                }

                isAuthenticatingWithGoogle = true;
                errorMessage = string.Empty;
                successMessage = "Starting Google login...";
                StateHasChanged();
                Debug.WriteLine("[Login] === GOOGLE AUTH FLOW STARTED ===");

                listener = new OAuthCallbackListener();
                var callbackTask = listener.StartAsync();
                Debug.WriteLine("[Login] Listener started on :5000");

                await Task.Delay(500);

                var url = GoogleAuthService.GetGoogleAuthUrl();
                Debug.WriteLine($"[Login] Opening browser to Google...");
                await Launcher.Default.OpenAsync(url);
                successMessage = "Waiting for Google login...";
                StateHasChanged();

                Debug.WriteLine("[Login] Waiting for callback from browser...");
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
                var completedTask = await Task.WhenAny(callbackTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Debug.WriteLine("[Login] TIMEOUT! No callback received in 5 minutes");
                    errorMessage = "Google login timed out";
                    return;
                }

                var query = await callbackTask;
                Debug.WriteLine($"[Login] Callback received! Query length: {query?.Length ?? 0}");

                if (string.IsNullOrEmpty(query))
                {
                    Debug.WriteLine("[Login] Query is EMPTY!");
                    errorMessage = "No callback from Google";
                    return;
                }

                var qp = System.Web.HttpUtility.ParseQueryString(query);
                var code = qp["code"];
                var state = qp["state"];
                var err = qp["error"];

                Debug.WriteLine($"[Login] Parsed: code={code?.Length ?? 0} chars, state={state?.Length ?? 0} chars, error={err}");

                if (!string.IsNullOrEmpty(err))
                {
                    errorMessage = $"Google error: {err}";
                    Debug.WriteLine($"[Login] Google returned error: {err}");
                    return;
                }

                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                {
                    errorMessage = "Incomplete Google response";
                    Debug.WriteLine($"[Login] MISSING code or state!");
                    return;
                }

                successMessage = "Authenticating...";
                StateHasChanged();
                Debug.WriteLine("[Login] Calling AuthenticateWithGoogleAsync...");

                var session = await GoogleAuthService.AuthenticateWithGoogleAsync(code, state);

                if (session == null)
                {
                    Debug.WriteLine("[Login] AuthenticateWithGoogleAsync returned NULL!");
                    errorMessage = "Failed to authenticate. Check Output window (View ? Output ? Debug)";
                    return;
                }

                Debug.WriteLine($"[Login] SUCCESS! User={session.UserName}, Role={session.Role}");

                // SAVE SESSION
                await SessionService.SaveSessionAsync(session);
                Debug.WriteLine("[Login] Session saved");

                successMessage = $"Welcome, {session.UserName}!";
                StateHasChanged();
                await Task.Delay(1000);

                var route = GetDashboardRoute(session.Role);
                Debug.WriteLine($"[Login] Navigating to {route}");
                Navigation.NavigateTo(route, forceLoad: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Login] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                Debug.WriteLine($"[Login] Stack: {ex.StackTrace}");
                errorMessage = $"Error: {ex.Message}";
            }
            finally
            {
                isAuthenticatingWithGoogle = false;
                listener?.Stop();
                Debug.WriteLine("[Login] === GOOGLE AUTH FLOW ENDED ===");
                StateHasChanged();
            }
        }

        private string GetDashboardRoute(string role)
        {
            return role switch
            {
                "Admin" => "/admin-dashboard",
                "Receptionist" => "/receptionist/dashboard",
                "Dentist" => "/dentist-dashboard",
                "Patient" => "/dashboard",
                _ => "/dashboard"
            };
        }

        public void Dispose()
        {
            try
            {
                if (SyncService != null)
                {
                    SyncService.SyncProgressChanged -= OnSyncProgressChanged;
                    SyncService.SyncCompleted -= OnSyncCompleted;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Dispose error: {ex}");
            }
        }
    }
}
