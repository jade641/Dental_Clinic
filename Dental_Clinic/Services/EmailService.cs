using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using Microsoft.Data.SqlClient; // replaces System.Data.SqlClient

namespace Dental_Clinic.Services
{
    public class EmailService
    {
        private readonly string _fromEmail;
        private readonly string _fromPassword;
        private readonly string _fromName;
        private readonly string _smtpServer;
        private readonly int _smtpPort;

        public EmailService(IConfiguration configuration)
        {
            // Read SMTP settings from configuration. Use safe defaults for development.
            _fromEmail = configuration["Smtp:FromEmail"] ?? "your-clinic-email@gmail.com";
            _fromPassword = configuration["Smtp:FromPassword"] ?? string.Empty;
            _fromName = configuration["Smtp:FromName"] ?? "Jade Dental Clinic";
            _smtpServer = configuration["Smtp:Server"] ?? "smtp.gmail.com";
            _smtpPort = int.TryParse(configuration["Smtp:Port"], out var p) ? p : 587;

            Debug.WriteLine($"[EmailService] Configured SMTP server: {_smtpServer}:{_smtpPort}, from: {_fromEmail}");
        }

        public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string resetToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(resetToken))
                {
                    Debug.WriteLine("[EmailService] Invalid email or token");
                    return false;
                }

                // Create reset link - update scheme/URL as needed
                var resetLink = $"dentalclinic://reset-password?token={resetToken}&email={Uri.EscapeDataString(toEmail)}";

                // Email subject and body
                var subject = "Password Reset Request - Jade Dental Clinic";
                var body = $@"<!DOCTYPE html>
<html>
<head>
 <style>
 body {{ font-family: Arial, sans-serif; line-height:1.6; color:#333; margin:0; padding:0; }}
 .container {{ max-width:600px; margin:0 auto; padding:20px; }}
 .header {{ background:linear-gradient(135deg,#0066FF 0%,#0052D9 100%); color:#fff; padding:30px; text-align:center; border-radius:8px 8px 0 0; }}
 .content {{ background:#f8f9fa; padding:30px; }}
 .button {{ display:inline-block; background:#0066FF; color:#fff; padding:12px 30px; text-decoration:none; border-radius:6px; font-weight:bold; margin:20px 0; }}
 .footer {{ text-align:center; padding:20px; color:#666; font-size:12px; }}
 </style>
</head>
<body>
 <div class='container'>
  <div class='header'>
   <h1>Jade Dental Clinic</h1>
   <p>Password Reset Request</p>
  </div>
  <div class='content'>
   <h2>Hello!</h2>
   <p>We received a request to reset your password for your Jade Dental Clinic account.</p>
   <p>Click the button below to reset your password:</p>
   <p style='text-align:center;'>
    <a href='{resetLink}' class='button'>Reset Password</a>
   </p>
   <p><strong>This link will expire in 1 hour.</strong></p>
   <p>If you didn't request this, please ignore this email or contact us if you have concerns.</p>
   <p>For security, the link can only be used once.</p>
  </div>
  <div class='footer'>
   <p>�2025 Jade Dental Clinic. All rights reserved.</p>
   <p>This is an automated email. Please do not reply.</p>
  </div>
 </div>
</body>
</html>";

                using var smtpClient = new SmtpClient(_smtpServer, _smtpPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(_fromEmail, _fromPassword)
                };

                if (string.IsNullOrEmpty(_fromPassword))
                {
                    Debug.WriteLine("[EmailService] SMTP password not configured.");
                    return false;
                }

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_fromEmail, _fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                mailMessage.To.Add(toEmail);

                await smtpClient.SendMailAsync(mailMessage);
                Debug.WriteLine($"[EmailService] Password reset email sent to {toEmail}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EmailService] Email send error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string body)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(toEmail)) return false;

                using var smtpClient = new SmtpClient(_smtpServer, _smtpPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(_fromEmail, _fromPassword)
                };

                if (string.IsNullOrEmpty(_fromPassword))
                {
                    Debug.WriteLine("[EmailService] SMTP password not configured.");
                    return false;
                }

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_fromEmail, _fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                mailMessage.To.Add(toEmail);

                await smtpClient.SendMailAsync(mailMessage);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EmailService] Error sending email: {ex.Message}");
                return false;
            }
        }

        public async Task<string?> GenerateAndSendResetTokenAsync(string email, DatabaseService databaseService)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(email)) return null;
                // Generate a 6-digit code
                var token = new Random().Next(100000, 999999).ToString();
                var expiryTime = DateTime.UtcNow.AddHours(1); // Token valid for 1 hour

                // Store token in database
                await StoreResetTokenAsync(email, token, expiryTime, databaseService);

                // Send email with code
                var emailSent = await SendVerificationCodeAsync(email, token, "Password Reset Code");

                return emailSent ? token : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EmailService] Error generating reset token: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> SendVerificationCodeAsync(string toEmail, string code, string subject = "Verification Code")
        {
            var body = $@"<!DOCTYPE html>
<html>
<head>
 <style>
 body {{ font-family: Arial, sans-serif; line-height:1.6; color:#333; }}
 .container {{ max-width:600px; margin:0 auto; padding:20px; border:1px solid #ddd; border-radius:8px; }}
 .header {{ background:#0066FF; color:#fff; padding:20px; text-align:center; border-radius:8px 8px 0 0; }}
 .content {{ padding:20px; text-align:center; }}
 .code {{ font-size:32px; font-weight:bold; color:#0066FF; letter-spacing:5px; margin:20px 0; }}
 .footer {{ text-align:center; font-size:12px; color:#666; margin-top:20px; }}
 </style>
</head>
<body>
 <div class='container'>
  <div class='header'>
   <h1>Jade Dental Clinic</h1>
  </div>
  <div class='content'>
   <h2>{subject}</h2>
   <p>Please use the following code to verify your request:</p>
   <div class='code'>{code}</div>
   <p>This code will expire in 1 hour.</p>
   <p>If you didn't request this, please ignore this email.</p>
  </div>
  <div class='footer'>
   <p>2025 Jade Dental Clinic. All rights reserved.</p>
  </div>
 </div>
</body>
</html>";
            return await SendEmailAsync(toEmail, subject, body);
        }

        private string GenerateSecureToken()
        {
            var randomBytes = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        private async Task StoreResetTokenAsync(string email, string token, DateTime expiryTime, DatabaseService databaseService)
        {
            try
            {
                var createTableQuery = @"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PasswordResetTokens')
BEGIN
  CREATE TABLE PasswordResetTokens (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Email NVARCHAR(100) NOT NULL,
    Token NVARCHAR(100) NOT NULL,
    ExpiryTime DATETIME NOT NULL,
    IsUsed BIT DEFAULT 0,
    CreatedAt DATETIME DEFAULT GETDATE()
  );
END";

                await databaseService.ExecuteNonQueryAsync(createTableQuery);

                var insertQuery = @"
INSERT INTO PasswordResetTokens (Email, Token, ExpiryTime, IsUsed)
VALUES (@Email, @Token, @ExpiryTime, 0)";

                var parameters = new[]
                {
                    new SqlParameter("@Email", email),
                    new SqlParameter("@Token", token),
                    new SqlParameter("@ExpiryTime", expiryTime)
                };

                await databaseService.ExecuteNonQueryAsync(insertQuery, parameters);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EmailService] Error storing reset token: {ex.Message}");
                throw; // rethrow so caller can handle
            }
        }


    }
}
