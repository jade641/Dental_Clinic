using System;

namespace Dental_Clinic.Models
{
    public class ChatBotConversation
    {
        public int ConversationID { get; set; }
        public int PatientID { get; set; }
        public int RecipientID { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? Status { get; set; } // "Open", "Closed", "Escalated"
        public int? EscalatedTo { get; set; }
        public string? EscalationReason { get; set; }
        public string? Resolution { get; set; }
    }

    public class ChatMessage
    {
        public long MessageID { get; set; }
        public int ConversationID { get; set; }
        public int SenderUserID { get; set; }
        public string Sender { get; set; } = string.Empty; // "Patient", "Bot", "Receptionist"
        public string MessageText { get; set; } = string.Empty;
        public DateTime? Timestamp { get; set; }
        public bool? IsRead { get; set; }
        public string? IntentDetected { get; set; }
    }
}
