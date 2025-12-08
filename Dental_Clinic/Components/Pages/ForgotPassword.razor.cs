using Microsoft.AspNetCore.Components;
using Dental_Clinic.Services;
using System.ComponentModel.DataAnnotations;

namespace Dental_Clinic.Components.Pages
{
   public partial class ForgotPassword
   {
      [Inject] private NavigationManager Navigation { get; set; } = default!;
      [Inject] private EmailService EmailService { get; set; } = default!;
      [Inject] private DatabaseService DatabaseService { get; set; } = default!;

      private ForgotPasswordModel forgotPasswordModel = new ForgotPasswordModel();
      private string errorMessage = string.Empty;
      private bool isLoading = false;
      private bool emailSent = false;

      private void BackToLogin()
      {
         Navigation.NavigateTo("/login");
      }

      private async Task HandleForgotPassword()
      {
         isLoading = true;
         errorMessage = string.Empty;

         try
         {
            // Generate and send reset token
            var token = await EmailService.GenerateAndSendResetTokenAsync(forgotPasswordModel.Email, DatabaseService);

            if (!string.IsNullOrEmpty(token))
            {
               // Email sent successfully
               emailSent = true;
               Navigation.NavigateTo($"/reset-password?email={forgotPasswordModel.Email}");
            }
            else
            {
               errorMessage = "Failed to send reset email. Please verify the email address and try again.";
            }
         }
         catch (Exception ex)
         {
            errorMessage = $"An error occurred: {ex.Message}";
            Console.WriteLine($"Forgot password error: {ex}");
         }
         finally
         {
            isLoading = false;
         }
      }

      public class ForgotPasswordModel
      {
         [Required(ErrorMessage = "Email is required")]
         [EmailAddress(ErrorMessage = "Invalid email address")]
         public string Email { get; set; } = string.Empty;
      }
   }
}
