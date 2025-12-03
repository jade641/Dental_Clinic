using System.ComponentModel.DataAnnotations;

namespace Dental_Clinic.Models
{
    public class LoginModel
    {
        [Required(ErrorMessage = "Email or Username is required")]
        public string EmailOrUsername { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }

    public class SignUpModel
    {
        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        [Required]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string PhoneNumber { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        public int? Age { get; set; }
        public string Sex { get; set; } = string.Empty;

        // Patient-specific fields
        public DateTime? BirthDate { get; set; }
        public string Address { get; set; } = string.Empty;
        public string MaritalStatus { get; set; } = string.Empty;
        public string MedicalHistory { get; set; } = string.Empty;
        public string InsuranceProvider { get; set; } = string.Empty;
        public string InsurancePolicyNumber { get; set; } = string.Empty;

        // Dentist-specific fields
        public string Specialization { get; set; } = string.Empty;
        public bool IsAvailable { get; set; } = true;

        // Common
        public string Role { get; set; } = "Patient"; // Default role
    }

    // This is what gets stored in session after successful login
    public class UserSession
    {
        public string Token { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public int UserId { get; set; }
        public int? RoleSpecificId { get; set; } // AdminID, DentistID, ReceptionistID, or PatientID
    }

    public class StaffLoginModel
    {
        public string EmailOrUsername { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string AccessLevel { get; set; } = "Dentist"; // Admin, Receptionist, Dentist
        public bool RememberMe { get; set; }
    }
}