using Microsoft.AspNetCore.Components;
using Dental_Clinic.Services;
using System.ComponentModel.DataAnnotations;

namespace Dental_Clinic.Components.Pages
{
    public partial class ResetPassword
    {
        [Parameter]
        [SupplyParameterFromQuery(Name = "token")]
        public string? ResetToken { get; set; }

        [Parameter]
        [SupplyParameterFromQuery(Name = "email")]
        public string? Email { get; set; }

        [Inject] private NavigationManager Navigation { get; set; }
        [Inject] private DatabaseService DatabaseService { get; set; }

        private ResetPasswordModel resetPasswordModel = new();
        private bool showNewPassword = false;
        private bool showConfirmPassword = false;
        private string errorMessage = string.Empty;
        private string successMessage = string.Empty;
        private bool passwordResetSuccessful = false;

        private string newPasswordType => showNewPassword ? "text" : "password";
        private string confirmPasswordType => showConfirmPassword ? "text" : "password";

        protected override void OnInitialized()
        {
            if (string.IsNullOrEmpty(ResetToken) || string.IsNullOrEmpty(Email))
            {
                errorMessage = "Invalid or expired reset link.";
            }
        }

        private void BackToLogin()
        {
            Navigation.NavigateTo("/login");
        }

        private void GoToLogin()
        {
            Navigation.NavigateTo("/login");
        }

        private void ToggleNewPassword() => showNewPassword = !showNewPassword;
        private void ToggleConfirmPassword() => showConfirmPassword = !showConfirmPassword;

        private async Task HandleResetPassword()
        {
            if (resetPasswordModel.NewPassword != resetPasswordModel.ConfirmPassword)
            {
                errorMessage = "Passwords do not match.";
                return;
            }

            errorMessage = string.Empty;
            successMessage = string.Empty;

            try
            {
                var result = await DatabaseService.ResetPasswordAsync(Email, ResetToken, resetPasswordModel.NewPassword);

                if (result.Success)
                {
                    passwordResetSuccessful = true;
                    successMessage = result.Message;
                }
                else
                {
                    errorMessage = result.Message;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"An error occurred: {ex.Message}";
            }
        }

        public class ResetPasswordModel
        {
            [Required(ErrorMessage = "New password is required")]
            [MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
            public string NewPassword { get; set; } = string.Empty;

            [Required(ErrorMessage = "Please confirm your password")]
            public string ConfirmPassword { get; set; } = string.Empty;
        }
    }
}
