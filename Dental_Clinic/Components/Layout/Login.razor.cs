using Microsoft.AspNetCore.Components;
using Dental_Clinic.Models;
using Dental_Clinic.Services;

namespace Dental_Clinic.Components.Layout
{
    public partial class Login
    {
        private bool isLoginMode = true;
        private bool showLoginPassword = false;
        private bool showSignupPassword = false;
        private bool showStaffPassword = false;
        private bool showStaffModal = false;
        private bool isLoading = false;
        private bool showSyncModal = false;

        private string errorMessage = string.Empty;
        private string successMessage = string.Empty;
        private string staffErrorMessage = string.Empty;
        private string syncMessage = string.Empty;
        private int syncProgress = 0;

        private LoginModel loginModel = new LoginModel();
        private SignUpModel signUpModel = new SignUpModel();
        private StaffLoginModel staffModel = new StaffLoginModel();

        private string loginPasswordType => showLoginPassword ? "text" : "password";
        private string signupPasswordType => showSignupPassword ? "text" : "password";
        private string staffPasswordType => showStaffPassword ? "text" : "password";

        [Inject] private LocalDatabaseService LocalDb { get; set; }
        [Inject] private SyncService SyncService { get; set; }

        protected override async Task OnInitializedAsync()
        {
            var uri = Navigation.ToAbsoluteUri(Navigation.Uri);
            if (uri.AbsolutePath.Contains("/signup") || uri.AbsolutePath.Contains("/auth"))
            {
                isLoginMode = uri.AbsolutePath.Contains("/login") || uri.AbsolutePath == "/";
            }

            // Initialize local database for offline support
            await LocalDb.InitializeLocalDatabaseAsync();
            
            // Initialize online database connection check
            await DatabaseService.InitializeDatabaseAsync();

            // Subscribe to sync events
            SyncService.SyncProgressChanged += OnSyncProgressChanged;
            SyncService.SyncCompleted += OnSyncCompleted;

            // Auto-switch to offline mode if no connection
            if (!DatabaseService.IsOnline)
            {
                await AuthService.SwitchToOfflineModeAsync();
            }
        }

        private void OnSyncProgressChanged(object sender, SyncProgressEventArgs e)
        {
            syncMessage = e.Message;
            syncProgress = e.Percentage;
            StateHasChanged();
        }

        private void OnSyncCompleted(object sender, SyncCompletedEventArgs e)
        {
            syncMessage = e.Result.Message;
            showSyncModal = false;
            StateHasChanged();
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

        private void ToggleLoginPassword() => showLoginPassword = !showLoginPassword;
        private void ToggleSignupPassword() => showSignupPassword = !showSignupPassword;
        private void ToggleStaffPassword() => showStaffPassword = !showStaffPassword;

        private void ToggleTheme()
        {
            // Implement theme toggle logic
        }

        private async Task ToggleOfflineMode()
        {
            if (AuthService.IsOfflineMode)
            {
                // Switch to online
                AuthService.SwitchToOnlineMode();
                successMessage = "Switched to Online Mode";
            }
            else
            {
                // Switch to offline
                await AuthService.SwitchToOfflineModeAsync();
                successMessage = "Switched to Offline Mode";
            }
            StateHasChanged();
        }

        private async Task StartFullSync()
        {
            showSyncModal = true;
            syncMessage = "Starting sync...";
            syncProgress = 0;

            await SyncService.FullSyncDownloadAsync();
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

        private void OpenStaffModal()
        {
            showStaffModal = true;
            staffErrorMessage = string.Empty;
        }

        private void CloseStaffModal()
        {
            showStaffModal = false;
            staffModel = new StaffLoginModel();
            staffErrorMessage = string.Empty;
        }

        private async Task HandleLogin()
        {
            isLoading = true;
            errorMessage = string.Empty;
            successMessage = string.Empty;

            try
            {
                var session = await AuthService.LoginAsync(loginModel);

                if (session != null)
                {
                    // Navigate based on role
                    string dashboardRoute = GetDashboardRoute(session.Role);
                    Navigation.NavigateTo(dashboardRoute);
                }
                else
                {
                    errorMessage = "Invalid credentials. Please try again.";
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Login failed: {ex.Message}";
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
            }
            finally
            {
                isLoading = false;
            }
        }

        private void HandleGoogleAuth()
        {
            // TODO: Implement Google authentication logic
            errorMessage = "Google authentication is not yet implemented.";
        }

        private async Task HandleStaffLogin()
        {
            isLoading = true;
            staffErrorMessage = string.Empty;

            try
            {
                var session = await AuthService.StaffLoginAsync(staffModel);

                if (session != null)
                {
                    string dashboardRoute = GetDashboardRoute(session.Role);
                    Navigation.NavigateTo(dashboardRoute);
                    CloseStaffModal();
                }
                else
                {
                    staffErrorMessage = $"Access denied. Invalid credentials or insufficient permissions for {staffModel.AccessLevel} role.";
                }
            }
            catch (Exception ex)
            {
                staffErrorMessage = $"Authentication failed: {ex.Message}";
            }
            finally
            {
                isLoading = false;
            }
        }

        private string GetDashboardRoute(string role)
        {
            return role switch
            {
                "Admin" => "/admin-dashboard",
                "Receptionist" => "/receptionist-dashboard",
                "Dentist" => "/dentist-dashboard",
                _ => "/dashboard"
            };
        }

        public void Dispose()
        {
            // Unsubscribe from sync events
            if (SyncService != null)
            {
                SyncService.SyncProgressChanged -= OnSyncProgressChanged;
                SyncService.SyncCompleted -= OnSyncCompleted;
            }
        }
    }
}
