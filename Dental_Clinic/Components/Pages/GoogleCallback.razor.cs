using Microsoft.AspNetCore.Components;
using Dental_Clinic.Services;

namespace Dental_Clinic.Components.Pages
{
    public partial class GoogleCallback
    {
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private GoogleAuthService GoogleAuthService { get; set; } = default!;
        [Inject] private AuthService AuthService { get; set; } = default!;

        private string errorMessage = string.Empty;

        private void BackToLogin()
        {
            Navigation.NavigateTo("/login");
        }

        protected override async Task OnInitializedAsync()
        {
            try
            {
                var uri = Navigation.ToAbsoluteUri(Navigation.Uri);

                // Handle both HTTP and custom URI scheme callbacks
                var query = uri.Query;
                if (query.StartsWith("?"))
                {
                    query = query.Substring(1);
                }

                var queryParams = new Dictionary<string, string>();
                foreach (var param in query.Split('&'))
                {
                    var parts = param.Split('=');
                    if (parts.Length == 2)
                    {
                        queryParams[parts[0]] = Uri.UnescapeDataString(parts[1]);
                    }
                }

                if (queryParams.ContainsKey("code") && queryParams.ContainsKey("state"))
                {
                    var code = queryParams["code"];
                    var state = queryParams["state"];

                    var session = await GoogleAuthService.AuthenticateWithGoogleAsync(code, state);

                    if (session != null)
                    {
                        // Store session
                        // TODO: Store in secure storage or session service

                        // Navigate to dashboard based on role
                        var dashboardRoute = session.Role switch
                        {
                            "Admin" => "/admin-dashboard",
                            "Receptionist" => "/receptionist-dashboard",
                            "Dentist" => "/dentist-dashboard",
                            _ => "/dashboard"
                        };

                        Navigation.NavigateTo(dashboardRoute);
                    }
                    else
                    {
                        errorMessage = "Google authentication failed. Please try again.";
                    }
                }
                else if (queryParams.ContainsKey("error"))
                {
                    errorMessage = $"Google authentication error: {queryParams["error"]}";
                }
                else
                {
                    errorMessage = "Invalid callback parameters. Please try signing in again.";
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Authentication error: {ex.Message}";
                Console.WriteLine($"GoogleCallback error: {ex}");
            }
        }
    }
}
