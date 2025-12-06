using System;

namespace Dental_Clinic.Models
{
    public class FeedbackDisplayModel
    {
        public int FeedbackID { get; set; }
        public int PatientID { get; set; }
        public string PatientName { get; set; } = "";
        public string PatientEmail { get; set; } = "";
        public string PatientAvatar { get; set; } = ""; // Initials or color class
        public int Rating { get; set; }
        public string FeedbackText { get; set; } = "";
        public DateTime Date { get; set; }
        public bool IsReplied { get; set; } // We might need to track this in DB or just assume false for now
    }

    public class CampaignModel
    {
        public int CampaignID { get; set; }
        public string CampaignName { get; set; } = "";
        public string Description { get; set; } = ""; // ContextTemplate
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int SentCount { get; set; }
        public string ImageUrl { get; set; } = "";
    }

    public class PatientEmailModel
    {
        public int PatientID { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }
}
