using System;

namespace Dental_Clinic.Models
{
    public class Feedback
    {
        public int FeedbackID { get; set; }
        public int PatientID { get; set; }
        public int? AppointmentID { get; set; }
        public DateTime SubmissionDate { get; set; }
        public int? RatingValue { get; set; }
        public string FeedbackText { get; set; }
    }
}
