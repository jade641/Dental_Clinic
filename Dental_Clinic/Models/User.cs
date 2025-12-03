using System;

namespace Dental_Clinic.Models
{
  public class User
  {
    public int UserID { get; set; }
    public string RoleName { get; set; } = string.Empty; // Admin, Dentist, Receptionist, Patient
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public int? Age { get; set; }
    public string Sex { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Computed properties
    public string FullName => $"{FirstName} {LastName}";

    // Role-specific IDs (populated based on role)
    public int? AdminID { get; set; }
    public int? ReceptionistID { get; set; }
    public int? DentistID { get; set; }
    public int? PatientID { get; set; }
  }

  // Admin model
  public class Admin
  {
    public int AdminID { get; set; }
    public int UserID { get; set; }
  }

  // Receptionist model
  public class Receptionist
  {
    public int ReceptionistID { get; set; }
    public int UserID { get; set; }
  }

  // Dentist model
  public class Dentist
  {
    public int DentistID { get; set; }
    public int UserID { get; set; }
    public string Specialization { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public string ProfileImg { get; set; } = string.Empty;
  }

  // Patient model
  public class Patient
  {
    public int PatientID { get; set; }
    public int UserID { get; set; }
    public string MedicalAlerts { get; set; } = string.Empty;
    public DateTime BirthDate { get; set; }
    public string Address { get; set; } = string.Empty;
    public string MaritalStatus { get; set; } = string.Empty;
    public string ProfileImg { get; set; } = string.Empty;
    public string MedicalHistory { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string InsuranceProvider { get; set; } = string.Empty;
    public string InsurancePolicyNumber { get; set; } = string.Empty;
  }
}
