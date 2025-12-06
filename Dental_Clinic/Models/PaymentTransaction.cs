using System;

namespace Dental_Clinic.Models
{
    public class PaymentTransaction
    {
        public int PaymentID { get; set; }
        public int PatientID { get; set; }
        public int? AppointmentID { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string? ReferenceNumber { get; set; }
        public string? Remarks { get; set; }
    }
}