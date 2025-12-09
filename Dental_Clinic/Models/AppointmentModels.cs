using System.ComponentModel.DataAnnotations;

namespace Dental_Clinic.Models
{
  public class Service
  {
    public int ServiceID { get; set; }
    public int? CategoryID { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Duration { get; set; }
    public decimal Cost { get; set; } // Add price/cost
    public bool IsActive { get; set; }

    // Navigation properties
    public string CategoryName { get; set; } = string.Empty;
  }

  public class ServiceCategory
  {
    public int CategoryID { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
  }

  public class Appointment
  {
    public int AppointmentID { get; set; }
    public int PatientID { get; set; }
    public int? DentistID { get; set; }
    public int? ServiceID { get; set; }
    public int? EventID { get; set; }
    public int? SlotID { get; set; } // Link to AvailableSlots
    public DateTime AppointmentDate { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string Status { get; set; } = "Scheduled"; // Scheduled, Confirmed, Completed, Cancelled, No-Show
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int? CanceledBy { get; set; }
    public string CancelationReason { get; set; } = string.Empty;
    public string NoShowReason { get; set; } = string.Empty;
    public DateTime? ModifiedAt { get; set; }

    // Navigation properties
    public string ServiceName { get; set; } = string.Empty;
    public string DentistName { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public string PatientEmail { get; set; } = string.Empty;
    public string? PatientAvatar { get; set; }
    public decimal ServiceCost { get; set; } // Added for billing
  }

  public class DentistAvailability
  {
    public int DentistID { get; set; }
    public string DentistName { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
  }

  public class TimeSlotModel
  {
    public int? SlotID { get; set; } // From AvailableSlots table
    public DateTime Date { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int DentistID { get; set; }
    public string DentistName { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public string FormattedTime => $"{FormatTime(StartTime)} - {FormatTime(EndTime)}";

    private string FormatTime(TimeSpan time)
    {
      var hours = time.Hours;
      var minutes = time.Minutes;
      var ampm = hours >= 12 ? "PM" : "AM";
      if (hours > 12) hours -= 12;
      if (hours == 0) hours = 12;
      return $"{hours}:{minutes:00} {ampm}";
    }
  }

  public class CreateAppointmentModel
  {
    [Required]
    public int PatientID { get; set; }

    [Required]
    public int ServiceID { get; set; }

    [Required]
    public int DentistID { get; set; }

    [Required]
    public DateTime AppointmentDate { get; set; }

    [Required]
    public TimeSpan StartTime { get; set; }

    [Required]
    public TimeSpan EndTime { get; set; }

    public int? SlotID { get; set; } // Link to AvailableSlots

    public string Notes { get; set; } = string.Empty;

    public string PatientName { get; set; } = string.Empty;
    public string PatientEmail { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
  }

  public class TimeSlotGroup
  {
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string FormattedTime => FormatTimeRange(StartTime, EndTime);
    public List<DentistSlot> AvailableDentists { get; set; } = new();

    private string FormatTimeRange(TimeSpan start, TimeSpan end)
    {
      return $"{FormatTime(start)} - {FormatTime(end)}";
    }

    private string FormatTime(TimeSpan time)
    {
      var hours = time.Hours;
      var minutes = time.Minutes;
      var ampm = hours >= 12 ? "PM" : "AM";
      if (hours > 12) hours -= 12;
      if (hours == 0) hours = 12;
      return $"{hours}:{minutes:D2} {ampm}";
    }
  }

  public class DentistSlot
  {
    public int DentistID { get; set; }
    public string DentistName { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public int? SlotID { get; set; }
  }

  public class Notification
  {
    public int NotificationID { get; set; }
    public int PatientID { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime DateCreated { get; set; }
    public bool IsRead { get; set; }
    public string Type { get; set; } = string.Empty;
  }
}
