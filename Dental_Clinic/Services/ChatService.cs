using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using Dental_Clinic.Models;

namespace Dental_Clinic.Services
{
    public class ChatService
    {
        private readonly DatabaseService _databaseService;

        public ChatService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public Dictionary<string, string> GetFAQs()
        {
            return new Dictionary<string, string>
            {
                { "Clinic Hours", "We are open Monday to Saturday, 8:00 AM - 6:00 PM. Closed on Sundays and public holidays." },
                { "Our Services", "We offer Teeth Cleaning, Root Canals, Braces, Tooth Extraction, and Dental Implants." },
                { "Pricing & Payment", "Consultation starts at ₱500. We accept Cash, Gcash, and major Credit Cards." },
                { "Book Appointment", "You can book an appointment directly through this dashboard by clicking 'Book Appointment'." },
                { "Location & Contact", "We are located at 123 Dental St., Smile City. Call us at (02) 8123-4567." },
                { "Emergency Care", "For emergencies, please visit our clinic immediately or call our emergency hotline at (02) 8999-9999." }
            };
        }

        public async Task<ChatBotConversation?> GetActiveConversationAsync(int patientId)
        {
            string query = @"
                SELECT TOP 1 ConversationID, PatientID, RecipientID, StartTime, EndTime, Status, EscalatedTo, EscalationReason, Resolution
                FROM ChatBotConversation
                WHERE PatientID = @PatientID AND (Status = 'Open' OR Status = 'Escalated')
                ORDER BY StartTime DESC";

            var parameters = new SqlParameter[]
            {
                new SqlParameter("@PatientID", patientId)
            };

            var dataTable = await _databaseService.ExecuteReaderAsync(query, parameters);

            if (dataTable.Rows.Count > 0)
            {
                var row = dataTable.Rows[0];
                return new ChatBotConversation
                {
                    ConversationID = Convert.ToInt32(row["ConversationID"]),
                    PatientID = Convert.ToInt32(row["PatientID"]),
                    RecipientID = Convert.ToInt32(row["RecipientID"]),
                    StartTime = row["StartTime"] as DateTime?,
                    EndTime = row["EndTime"] as DateTime?,
                    Status = row["Status"].ToString(),
                    EscalatedTo = row["EscalatedTo"] as int?,
                    EscalationReason = row["EscalationReason"] as string,
                    Resolution = row["Resolution"] as string
                };
            }

            return null;
        }

        public async Task<int> CreateConversationAsync(int patientId)
        {
            // Assuming RecipientID 0 is for Bot/System initially
            string query = @"
                INSERT INTO ChatBotConversation (PatientID, RecipientID, StartTime, Status)
                VALUES (@PatientID, 0, GETDATE(), 'Open');
                SELECT CAST(SCOPE_IDENTITY() as int);";

            var parameters = new SqlParameter[]
            {
                new SqlParameter("@PatientID", patientId)
            };

            var result = await _databaseService.ExecuteScalarAsync(query, parameters);
            return Convert.ToInt32(result);
        }

        public async Task<List<ChatMessage>> GetMessagesAsync(int conversationId)
        {
            string query = @"
                SELECT MessageID, ConversationID, SenderUserID, Sender, MessageText, Timestamp, IsRead, IntentDetected
                FROM ChatMessages
                WHERE ConversationID = @ConversationID
                ORDER BY Timestamp ASC";

            var parameters = new SqlParameter[]
            {
                new SqlParameter("@ConversationID", conversationId)
            };

            var dataTable = await _databaseService.ExecuteReaderAsync(query, parameters);
            var messages = new List<ChatMessage>();

            foreach (DataRow row in dataTable.Rows)
            {
                messages.Add(new ChatMessage
                {
                    MessageID = Convert.ToInt64(row["MessageID"]),
                    ConversationID = Convert.ToInt32(row["ConversationID"]),
                    SenderUserID = Convert.ToInt32(row["SenderUserID"]),
                    Sender = row["Sender"].ToString() ?? "",
                    MessageText = row["MessageText"].ToString() ?? "",
                    Timestamp = row["Timestamp"] as DateTime?,
                    IsRead = row["IsRead"] as bool?,
                    IntentDetected = row["IntentDetected"] as string
                });
            }

            return messages;
        }

        public async Task SendMessageAsync(int conversationId, int senderUserId, string senderName, string messageText, string? intent = null)
        {
            string query = @"
                INSERT INTO ChatMessages (ConversationID, SenderUserID, Sender, MessageText, Timestamp, IsRead, IntentDetected)
                VALUES (@ConversationID, @SenderUserID, @Sender, @MessageText, GETDATE(), 0, @Intent)";

            var parameters = new SqlParameter[]
            {
                new SqlParameter("@ConversationID", conversationId),
                new SqlParameter("@SenderUserID", senderUserId),
                new SqlParameter("@Sender", senderName),
                new SqlParameter("@MessageText", messageText),
                new SqlParameter("@Intent", intent ?? (object)DBNull.Value)
            };

            await _databaseService.ExecuteNonQueryAsync(query, parameters);
        }

        public async Task EscalateConversationAsync(int conversationId, string reason)
        {
            string query = @"
                UPDATE ChatBotConversation
                SET Status = 'Escalated', EscalationReason = @Reason
                WHERE ConversationID = @ConversationID";

            var parameters = new SqlParameter[]
            {
                new SqlParameter("@ConversationID", conversationId),
                new SqlParameter("@Reason", reason)
            };

            await _databaseService.ExecuteNonQueryAsync(query, parameters);
        }

        public class ConversationViewModel
        {
            public int ConversationID { get; set; }
            public int PatientID { get; set; }
            public string PatientName { get; set; } = string.Empty;
            public string LastMessage { get; set; } = string.Empty;
            public DateTime? LastMessageTime { get; set; }
            public int UnreadCount { get; set; }
            public string Status { get; set; } = string.Empty;
            public string Avatar { get; set; }
        }

        public async Task<List<ConversationViewModel>> GetReceptionistConversationsAsync()
        {
            string query = @"
                SELECT 
                    c.ConversationID,
                    c.PatientID,
                    ISNULL(u.FirstName + ' ' + u.LastName, u.UserName) as PatientName,
                    c.Status,
                    (SELECT TOP 1 MessageText FROM ChatMessages m WHERE m.ConversationID = c.ConversationID ORDER BY Timestamp DESC) as LastMessage,
                    (SELECT TOP 1 Timestamp FROM ChatMessages m WHERE m.ConversationID = c.ConversationID ORDER BY Timestamp DESC) as LastMessageTime,
                    (SELECT COUNT(*) FROM ChatMessages m WHERE m.ConversationID = c.ConversationID AND IsRead = 0 AND SenderUserID = c.PatientID) as UnreadCount,
                    u.Avatar
                FROM ChatBotConversation c
                LEFT JOIN Patient p ON c.PatientID = p.PatientID
                LEFT JOIN Users u ON p.UserID = u.UserID
                ORDER BY LastMessageTime DESC";

            var dataTable = await _databaseService.ExecuteReaderAsync(query, null);
            var list = new List<ConversationViewModel>();

            foreach (DataRow row in dataTable.Rows)
            {
                list.Add(new ConversationViewModel
                {
                    ConversationID = Convert.ToInt32(row["ConversationID"]),
                    PatientID = Convert.ToInt32(row["PatientID"]),
                    PatientName = row["PatientName"]?.ToString() ?? "Unknown",
                    Status = row["Status"]?.ToString() ?? "",
                    LastMessage = row["LastMessage"]?.ToString() ?? "",
                    LastMessageTime = row["LastMessageTime"] as DateTime?,
                    UnreadCount = row["UnreadCount"] != DBNull.Value ? Convert.ToInt32(row["UnreadCount"]) : 0,
                    Avatar = row["Avatar"] != DBNull.Value ? row["Avatar"].ToString() : null
                });
            }

            return list;
        }

        public async Task MarkMessagesAsReadAsync(int conversationId)
        {
            string query = "UPDATE ChatMessages SET IsRead = 1 WHERE ConversationID = @ConversationID AND IsRead = 0";
            var parameters = new SqlParameter[]
            {
                new SqlParameter("@ConversationID", conversationId)
            };
            await _databaseService.ExecuteNonQueryAsync(query, parameters);
        }
    }
}
