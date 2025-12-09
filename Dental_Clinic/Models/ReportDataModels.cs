using System;

namespace Dental_Clinic.Models
{
    public class FinancialReportModel
    {
        public int Year { get; set; }
        public string Month { get; set; } = string.Empty;
        public int TotalTransactions { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalStandardCost { get; set; }
        public decimal GrossProfit { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
    }

    public class ServiceReportModel
    {
        public string ServiceName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public int TimesPerformed { get; set; }
        public decimal TotalRevenueGenerated { get; set; }
        public decimal AveragePrice { get; set; }
        public int EstDuration { get; set; }
    }

    public class PatientReportModel
    {
        public int PatientID { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Sex { get; set; } = string.Empty;
        public int? Age { get; set; }
        public string Address { get; set; } = string.Empty;
        public int TotalVisits { get; set; }
        public DateTime? LastVisitDate { get; set; }
        public decimal OutstandingBalance { get; set; }
        public string AccountStatus { get; set; } = string.Empty;
    }

    public class StaffReportModel
    {
        public int DentistID { get; set; }
        public string DentistName { get; set; } = string.Empty;
        public string Specialization { get; set; } = string.Empty;
        public int TotalAppointmentsAssigned { get; set; }
        public int CompletedAppointments { get; set; }
        public int CancelledAppointments { get; set; }
        public decimal RevenueGenerated { get; set; }
    }

    public class AuditTrailModel
    {
        public string ActivityType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PerformedBy { get; set; } = string.Empty;
        public DateTime? ActivityDate { get; set; }
    }
}
