using System.Data;
using Microsoft.Data.SqlClient;
using Dental_Clinic.Models;

namespace Dental_Clinic.Services
{
  public class AppointmentService
  {
    private readonly DatabaseService _onlineDb;
    private readonly LocalDatabaseService _localDb;
    private bool _useOfflineMode;

    public AppointmentService(DatabaseService onlineDb, LocalDatabaseService localDb)
    {
      _onlineDb = onlineDb;
      _localDb = localDb;
      _useOfflineMode = !onlineDb.IsOnline;
    }

    public string LastError { get; private set; } = string.Empty;

    public void SwitchToOnline()
    {
      if (_onlineDb.RetryConnection())
      {
        _useOfflineMode = false;
      }
    }

    public bool IsOffline => _useOfflineMode || !_onlineDb.IsOnline;

    #region Services

    public async Task<List<Service>> GetAllServicesAsync()
    {
      var services = new List<Service>();

      try
      {
        string query = @"
       SELECT s.ServiceID, s.CategoryID, s.ServiceName, s.Description, s.Duration, s.Cost, s.IsActive,
      ISNULL(sc.CategoryName, 'General') as CategoryName
     FROM Services s
    LEFT JOIN ServiceCategory sc ON s.CategoryID = sc.CategoryID
   WHERE s.IsActive = 1";

        System.Diagnostics.Debug.WriteLine($"[AppointmentService] Loading services. IsOffline: {IsOffline}");
        System.Diagnostics.Debug.WriteLine($"[AppointmentService] Database connection: {(_onlineDb.IsOnline ? "ONLINE" : "OFFLINE")}");

        DataTable dt;
        if (IsOffline)
        {
          System.Diagnostics.Debug.WriteLine("[AppointmentService] Using LOCAL database");
          dt = await _localDb.GetDataTableAsync(query);
        }
        else
        {
          System.Diagnostics.Debug.WriteLine("[AppointmentService] Using ONLINE database");
          dt = await _onlineDb.ExecuteReaderAsync(query);
        }

        System.Diagnostics.Debug.WriteLine($"[AppointmentService] Retrieved {dt.Rows.Count} services from database");

        if (dt.Rows.Count == 0)
        {
          System.Diagnostics.Debug.WriteLine("[AppointmentService] WARNING: No services found in database!");
          System.Diagnostics.Debug.WriteLine("[AppointmentService] Please check:");
          System.Diagnostics.Debug.WriteLine("[AppointmentService] 1. Services table has data");
          System.Diagnostics.Debug.WriteLine("[AppointmentService] 2. IsActive = 1 for services");
          System.Diagnostics.Debug.WriteLine("[AppointmentService] 3. Database connection is working");
        }

        foreach (DataRow row in dt.Rows)
        {
          // Parse Duration - handle both integer and string formats like "30 mins"
          int duration = 60; // Default
          var durationValue = row["Duration"];

          if (durationValue != DBNull.Value)
          {
            var durationStr = durationValue.ToString()?.Trim();
            if (!string.IsNullOrEmpty(durationStr))
            {
              // Remove "mins", "min", "minutes", etc. and parse the number
              durationStr = System.Text.RegularExpressions.Regex.Replace(durationStr, @"[^\d]", "");
              if (int.TryParse(durationStr, out int parsedDuration))
              {
                duration = parsedDuration;
              }
            }
          }

          // Parse Cost
          decimal cost = 0;
          if (row["Cost"] != DBNull.Value)
          {
            if (decimal.TryParse(row["Cost"].ToString(), out decimal parsedCost))
            {
              cost = parsedCost;
            }
          }

          var service = new Service
          {
            ServiceID = Convert.ToInt32(row["ServiceID"]),
            CategoryID = row["CategoryID"] != DBNull.Value ? Convert.ToInt32(row["CategoryID"]) : null,
            ServiceName = row["ServiceName"] != DBNull.Value ? row["ServiceName"].ToString() ?? string.Empty : string.Empty,
            Description = row["Description"]?.ToString() ?? "",
            Duration = duration,
            Cost = cost,
            IsActive = Convert.ToBoolean(row["IsActive"]),
            CategoryName = row["CategoryName"]?.ToString() ?? "General"
          };

          services.Add(service);
          System.Diagnostics.Debug.WriteLine($"[AppointmentService] Loaded service: {service.ServiceName} (ID: {service.ServiceID}, Duration: {service.Duration}min, Cost: ?{service.Cost})");
        }

        System.Diagnostics.Debug.WriteLine($"[AppointmentService] Successfully loaded {services.Count} services");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[AppointmentService] ERROR getting services: {ex.Message}");
        System.Diagnostics.Debug.WriteLine($"[AppointmentService] Stack trace: {ex.StackTrace}");
      }

      return services;
    }

    public async Task<Service?> GetServiceByIdAsync(int serviceId)
    {
      try
      {
        string query = @"
          SELECT s.ServiceID, s.CategoryID, s.ServiceName, s.Description, s.Duration, s.IsActive,
   sc.CategoryName
              FROM Services s
    LEFT JOIN ServiceCategory sc ON s.CategoryID = sc.CategoryID
       WHERE s.ServiceID = @ServiceID";

        var parameters = new[] { new SqlParameter("@ServiceID", serviceId) };

        DataTable dt;
        if (IsOffline)
        {
          dt = await _localDb.GetDataTableAsync(query, parameters);
        }
        else
        {
          dt = await _onlineDb.ExecuteReaderAsync(query, parameters);
        }

        if (dt.Rows.Count > 0)
        {
          var row = dt.Rows[0];
          return new Service
          {
            ServiceID = Convert.ToInt32(row["ServiceID"]),
            CategoryID = row["CategoryID"] != DBNull.Value ? Convert.ToInt32(row["CategoryID"]) : null,
            ServiceName = row["ServiceName"] != DBNull.Value ? row["ServiceName"].ToString() ?? string.Empty : string.Empty,
            Description = row["Description"] != DBNull.Value ? row["Description"].ToString() ?? string.Empty : string.Empty,
            Duration = Convert.ToInt32(row["Duration"]),
            IsActive = Convert.ToBoolean(row["IsActive"]),
            CategoryName = row["CategoryName"] != DBNull.Value && row["CategoryName"] != null ? row["CategoryName"].ToString() ?? string.Empty : string.Empty
          };
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error getting service: {ex.Message}");
      }

      return null;
    }

    #endregion

    #region Dentists

    public async Task<List<DentistAvailability>> GetAvailableDentistsAsync(DateTime date)
    {
      var dentists = new List<DentistAvailability>();

      try
      {
        string query = @"
           SELECT d.DentistID, d.Specialization, d.IsAvailable,
   u.FirstName, u.LastName
         FROM Dentist d
     INNER JOIN Users u ON d.UserID = u.UserID
 WHERE d.IsAvailable = 1";

        DataTable dt;
        if (IsOffline)
        {
          dt = await _localDb.GetDataTableAsync(query);
        }
        else
        {
          dt = await _onlineDb.ExecuteReaderAsync(query);
        }

        foreach (DataRow row in dt.Rows)
        {
          if (row["Specialization"] != DBNull.Value)
          {
            dentists.Add(new DentistAvailability
            {
              DentistID = Convert.ToInt32(row["DentistID"]),
              DentistName = $"Dr. {row["FirstName"]} {row["LastName"]}",
              Specialization = row["Specialization"] != DBNull.Value ? row["Specialization"].ToString() ?? string.Empty : string.Empty,
              IsAvailable = Convert.ToBoolean(row["IsAvailable"])
            });
          }
          else
          {
            dentists.Add(new DentistAvailability
            {
              DentistID = Convert.ToInt32(row["DentistID"]),
              DentistName = $"Dr. {row["FirstName"]} {row["LastName"]}",
              Specialization = "",
              IsAvailable = Convert.ToBoolean(row["IsAvailable"])
            });
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error getting dentists: {ex.Message}");
      }

      return dentists;
    }

    #endregion

    #region Time Slots

    public async Task<List<TimeSlotModel>> GetAvailableTimeSlotsAsync(DateTime date, int? serviceId = null)
    {
      var timeSlots = new List<TimeSlotModel>();

      try
      {
        // Get all dentists
        var dentists = await GetAvailableDentistsAsync(date);

        // Get service duration if provided
        int serviceDuration = 60; // Default
        if (serviceId.HasValue)
        {
          var service = await GetServiceByIdAsync(serviceId.Value);
          serviceDuration = service?.Duration ?? 60;
        }

        // Get existing appointments for this date to check what's booked
        string bookedQuery = @"
          SELECT DentistID, StartTime, EndTime 
    FROM Appointments
       WHERE CAST(AppointmentDate AS DATE) = @Date 
    AND Status NOT IN ('Cancelled', 'No-Show')";

        var parameters = new[] { new SqlParameter("@Date", date.Date) };

        DataTable bookedAppointments;
        if (IsOffline)
        {
          bookedAppointments = await _localDb.GetDataTableAsync(bookedQuery, parameters);
        }
        else
        {
          bookedAppointments = await _onlineDb.ExecuteReaderAsync(bookedQuery, parameters);
        }

        System.Diagnostics.Debug.WriteLine($"[AppointmentService] Found {bookedAppointments.Rows.Count} booked appointments for {date:yyyy-MM-dd}");

        // Generate time slots (8 AM to 5 PM, 1-hour intervals)
        var startHour = 8;
        var endHour = 17;

        foreach (var dentist in dentists)
        {
          for (int hour = startHour; hour < endHour; hour++)
          {
            var slotStart = new TimeSpan(hour, 0, 0);
            var slotEnd = slotStart.Add(TimeSpan.FromMinutes(serviceDuration));

            // Don't create slots that end after closing time
            if (slotEnd.Hours >= endHour)
              continue;

            // Check if this time slot is already booked
            bool isBooked = false;
            foreach (DataRow bookedRow in bookedAppointments.Rows)
            {
              var bookedDentistID = Convert.ToInt32(bookedRow["DentistID"]);
              var bookedStart = (TimeSpan)bookedRow["StartTime"];
              var bookedEnd = (TimeSpan)bookedRow["EndTime"];

              if (bookedDentistID == dentist.DentistID &&
                  ((slotStart >= bookedStart && slotStart < bookedEnd) ||
                (slotEnd > bookedStart && slotEnd <= bookedEnd) ||
                (slotStart <= bookedStart && slotEnd >= bookedEnd)))
              {
                isBooked = true;
                break;
              }
            }

            timeSlots.Add(new TimeSlotModel
            {
              SlotID = null, // No SlotID yet - will be created on booking
              Date = date,
              StartTime = slotStart,
              EndTime = slotEnd,
              DentistID = dentist.DentistID,
              DentistName = dentist.DentistName,
              IsAvailable = !isBooked
            });
          }
        }

        System.Diagnostics.Debug.WriteLine($"[AppointmentService] Generated {timeSlots.Count} time slots ({timeSlots.Count(t => t.IsAvailable)} available)");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error generating time slots: {ex.Message}");
        System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
      }

      return timeSlots.OrderBy(ts => ts.StartTime).ThenBy(ts => ts.DentistName).ToList();
    }

    public async Task<List<TimeSlotGroup>> GetGroupedTimeSlotsAsync(DateTime date, int? serviceId = null)
    {
      var timeSlots = await GetAvailableTimeSlotsAsync(date, serviceId);

      var grouped = timeSlots
          .GroupBy(slot => new { slot.StartTime, slot.EndTime })
     .Select(group => new TimeSlotGroup
     {
       StartTime = group.Key.StartTime,
       EndTime = group.Key.EndTime,
       AvailableDentists = group.Select(slot => new DentistSlot
       {
         DentistID = slot.DentistID,
         DentistName = slot.DentistName,
         Specialization = "", // Can be populated if needed
         IsAvailable = slot.IsAvailable,
         SlotID = slot.SlotID
       }).ToList()
     })
          .OrderBy(g => g.StartTime)
          .ToList();

      System.Diagnostics.Debug.WriteLine($"[AppointmentService] Grouped into {grouped.Count} time slots with multiple dentist options");

      return grouped;
    }

    #endregion

    #region Appointments

    public async Task<List<Appointment>> GetPatientAppointmentsAsync(int patientId)
    {
      var appointments = new List<Appointment>();

      try
      {
        string query = @"
        SELECT a.AppointmentID, a.PatientID, a.DentistID, a.ServiceID, a.AppointmentDate,
  a.StartTime, a.EndTime, a.Status, a.Notes,
           s.ServiceName,
    CONCAT(u.FirstName, ' ', u.LastName) as DentistName
      FROM Appointments a
         LEFT JOIN Services s ON a.ServiceID = s.ServiceID
 LEFT JOIN Dentist d ON a.DentistID = d.DentistID
    LEFT JOIN Users u ON d.UserID = u.UserID
      WHERE a.PatientID = @PatientID
      ORDER BY a.AppointmentDate DESC, a.StartTime DESC";

        var parameters = new[] { new SqlParameter("@PatientID", patientId) };

        DataTable dt;
        if (IsOffline)
        {
          dt = await _localDb.GetDataTableAsync(query, parameters);
        }
        else
        {
          dt = await _onlineDb.ExecuteReaderAsync(query, parameters);
        }

        foreach (DataRow row in dt.Rows)
        {
          appointments.Add(MapAppointmentFromRow(row));
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error getting appointments: {ex.Message}");
      }

      return appointments;
    }

    private async Task<List<Appointment>> GetAppointmentsByDateAsync(DateTime date)
    {
      var appointments = new List<Appointment>();

      try
      {
        string query = @"
    SELECT a.AppointmentID, a.PatientID, a.DentistID, a.ServiceID, a.AppointmentDate,
     a.StartTime, a.EndTime, a.Status, a.Notes,
s.ServiceName,
     CONCAT(u.FirstName, ' ', u.LastName) as DentistName
  FROM Appointments a
    LEFT JOIN Services s ON a.ServiceID = s.ServiceID
  LEFT JOIN Dentist d ON a.DentistID = d.DentistID
   LEFT JOIN Users u ON d.UserID = u.UserID
       WHERE CAST(a.AppointmentDate AS DATE) = @Date";

        var parameters = new[] { new SqlParameter("@Date", date.Date) };

        DataTable dt;
        if (IsOffline)
        {
          dt = await _localDb.GetDataTableAsync(query, parameters);
        }
        else
        {
          dt = await _onlineDb.ExecuteReaderAsync(query, parameters);
        }

        foreach (DataRow row in dt.Rows)
        {
          appointments.Add(MapAppointmentFromRow(row));
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error getting appointments by date: {ex.Message}");
      }

      return appointments;
    }

    public async Task<List<Appointment>> GetAppointmentsInRangeAsync(DateTime startDate, DateTime endDate)
    {
      var appointments = new List<Appointment>();

      try
      {
        string query = @"
SELECT a.AppointmentID, a.PatientID, a.DentistID, a.ServiceID, a.AppointmentDate,
 a.StartTime, a.EndTime, a.Status, a.Notes,
 s.ServiceName,
 CONCAT(du.FirstName, ' ', du.LastName) AS DentistName,
 CONCAT(pu.FirstName, ' ', pu.LastName) AS PatientName,
 pu.Email AS PatientEmail,
 pu.PhoneNumber AS PatientPhone
FROM Appointments a
LEFT JOIN Services s ON a.ServiceID = s.ServiceID
LEFT JOIN Dentist d ON a.DentistID = d.DentistID
LEFT JOIN Users du ON d.UserID = du.UserID
LEFT JOIN Users pu ON a.PatientID = pu.UserID
WHERE CAST(a.AppointmentDate AS DATE) BETWEEN @Start AND @End";

        var parameters = new[]
        {
 new SqlParameter("@Start", startDate.Date),
 new SqlParameter("@End", endDate.Date)
 };

        DataTable dt;
        if (IsOffline)
        {
          dt = await _localDb.GetDataTableAsync(query, parameters);
        }
        else
        {
          dt = await _onlineDb.ExecuteReaderAsync(query, parameters);
        }

        foreach (DataRow row in dt.Rows)
        {
          appointments.Add(MapAppointmentFromRow(row));
        }
      }
      catch (Exception ex)
      {
        LastError = ex.Message;
        System.Diagnostics.Debug.WriteLine($"Error getting appointments in range: {ex.Message}");
      }

      return appointments;
    }

    public async Task<(bool Success, string Message, int? AppointmentID)> CreateAppointmentAsync(CreateAppointmentModel model)
    {
      try
      {
        System.Diagnostics.Debug.WriteLine($"[AppointmentService] ==> BOOKING APPOINTMENT");
        System.Diagnostics.Debug.WriteLine($"[AppointmentService] Date: {model.AppointmentDate:yyyy-MM-dd}, Time: {model.StartTime}-{model.EndTime}, Dentist: {model.DentistID}");

        // Check if this time slot is already booked
        string checkQuery = @"
        SELECT AppointmentID FROM Appointments 
        WHERE DentistID = @DentistID 
 AND CAST(AppointmentDate AS DATE) = @Date
       AND StartTime = @StartTime 
   AND EndTime = @EndTime 
  AND Status NOT IN ('Cancelled', 'No-Show')";

        var checkParams = new[]
      {
     new SqlParameter("@DentistID", model.DentistID),
    new SqlParameter("@Date", model.AppointmentDate.Date),
  new SqlParameter("@StartTime", model.StartTime),
           new SqlParameter("@EndTime", model.EndTime)
        };

        DataTable existingAppointment;
        if (IsOffline)
        {
          existingAppointment = await _localDb.GetDataTableAsync(checkQuery, checkParams);
        }
        else
        {
          existingAppointment = await _onlineDb.ExecuteReaderAsync(checkQuery, checkParams);
        }

        if (existingAppointment.Rows.Count > 0)
        {
          System.Diagnostics.Debug.WriteLine($"[AppointmentService] ERROR: Time slot already booked");
          return (false, "This time slot has already been booked. Please choose another time.", null);
        }

        // Create appointment first
        string appointmentQuery = @"
 INSERT INTO Appointments (PatientID, DentistID, ServiceID, AppointmentDate, StartTime, EndTime, Status, Notes)
 OUTPUT INSERTED.AppointmentID
   VALUES (@PatientID, @DentistID, @ServiceID, @AppointmentDate, @StartTime, @EndTime, @Status, @Notes)";

        var appointmentParams = new[]
         {
    new SqlParameter("@PatientID", model.PatientID),
 new SqlParameter("@DentistID", model.DentistID),
   new SqlParameter("@ServiceID", model.ServiceID),
 new SqlParameter("@AppointmentDate", model.AppointmentDate.Date),
   new SqlParameter("@StartTime", model.StartTime),
  new SqlParameter("@EndTime", model.EndTime),
    new SqlParameter("@Status", "Scheduled"),
     new SqlParameter("@Notes", model.Notes ?? (object)DBNull.Value)
    };

        System.Diagnostics.Debug.WriteLine($"[AppointmentService] Creating appointment record...");

        int appointmentId;
        if (IsOffline)
        {
          appointmentId = Convert.ToInt32(await _localDb.ExecuteScalarAsync(appointmentQuery, appointmentParams));
          await _localDb.LogChangeAsync("Appointments", appointmentId, "INSERT");
        }
        else
        {
          appointmentId = Convert.ToInt32(await _onlineDb.ExecuteScalarAsync(appointmentQuery, appointmentParams));
        }

        System.Diagnostics.Debug.WriteLine($"[AppointmentService] ? Appointment created - AppointmentID: {appointmentId}");

        // Now create the slot record and mark it as booked (IsAvailable = 0)
        string slotQuery = @"
 INSERT INTO AvailableSlots (DentistID, SlotDate, StartTime, EndTime, IsAvailable)
   OUTPUT INSERTED.SlotID
    VALUES (@DentistID, @SlotDate, @StartTime, @EndTime, 0)";

        var slotParams = new[]
         {
          new SqlParameter("@DentistID", model.DentistID),
      new SqlParameter("@SlotDate", model.AppointmentDate.Date),
     new SqlParameter("@StartTime", model.StartTime),
        new SqlParameter("@EndTime", model.EndTime)
     };

        System.Diagnostics.Debug.WriteLine($"[AppointmentService] Creating slot record (marked as booked)...");

        int slotId;
        if (IsOffline)
        {
          slotId = Convert.ToInt32(await _localDb.ExecuteScalarAsync(slotQuery, slotParams));
          await _localDb.LogChangeAsync("AvailableSlots", slotId, "INSERT");
        }
        else
        {
          slotId = Convert.ToInt32(await _onlineDb.ExecuteScalarAsync(slotQuery, slotParams));
        }

        System.Diagnostics.Debug.WriteLine($"[AppointmentService] ? Slot created - SlotID: {slotId}");

        // Update appointment with SlotID
        string updateQuery = "UPDATE Appointments SET SlotID = @SlotID WHERE AppointmentID = @AppointmentID";
        var updateParams = new[]
           {
       new SqlParameter("@SlotID", slotId),
         new SqlParameter("@AppointmentID", appointmentId)
};

        if (IsOffline)
        {
          await _localDb.ExecuteNonQueryAsync(updateQuery, updateParams);
        }
        else
        {
          await _onlineDb.ExecuteNonQueryAsync(updateQuery, updateParams);
        }

        System.Diagnostics.Debug.WriteLine($"[AppointmentService] ==> BOOKING COMPLETE - AppointmentID: {appointmentId}, SlotID: {slotId}");
        return (true, "Appointment booked successfully!", appointmentId);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[AppointmentService] ==> ERROR creating appointment: {ex.Message}");
        System.Diagnostics.Debug.WriteLine($"[AppointmentService] Stack trace: {ex.StackTrace}");
        return (false, $"Failed to create appointment: {ex.Message}", null);
      }
    }

    public async Task<(bool Success, string Message)> CancelAppointmentAsync(int appointmentId, int canceledBy, string reason)
    {
      try
      {
        string query = @"
      UPDATE Appointments
     SET Status = 'Cancelled',
       CancellationReason = @Reason,
          ModifiedDate = @ModifiedDate
            WHERE AppointmentID = @AppointmentID";

        var parameters = new[]
          {
    new SqlParameter("@AppointmentID", appointmentId),
          new SqlParameter("@Reason", reason ?? (object)DBNull.Value),
            new SqlParameter("@ModifiedDate", DateTime.Now)
      };

        int rowsAffected;
        if (IsOffline)
        {
          rowsAffected = await _localDb.ExecuteNonQueryAsync(query, parameters);
          await _localDb.LogChangeAsync("Appointments", appointmentId, "UPDATE");
        }
        else
        {
          rowsAffected = await _onlineDb.ExecuteNonQueryAsync(query, parameters);
        }

        return rowsAffected > 0
        ? (true, "Appointment cancelled successfully")
        : (false, "Appointment not found");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error cancelling appointment: {ex.Message}");
        return (false, $"Failed to cancel appointment: {ex.Message}");
      }
    }

    public async Task<(bool Success, string Message)> ConfirmAppointmentAsync(int appointmentId)
    {
      try
      {
        string query = @"UPDATE Appointments SET Status = 'Confirmed', ModifiedDate = @ModifiedDate WHERE AppointmentID = @AppointmentID";
        var parameters = new[] {
 new SqlParameter("@AppointmentID", appointmentId),
 new SqlParameter("@ModifiedDate", DateTime.Now)
 };

        int rows;
        if (IsOffline)
        {
          rows = await _localDb.ExecuteNonQueryAsync(query, parameters);
          await _localDb.LogChangeAsync("Appointments", appointmentId, "UPDATE");
        }
        else
        {
          rows = await _onlineDb.ExecuteNonQueryAsync(query, parameters);
        }

        return rows > 0 ? (true, "Appointment confirmed") : (false, "Appointment not found");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error confirming appointment: {ex.Message}");
        return (false, $"Failed to confirm appointment: {ex.Message}");
      }
    }

    public async Task<(bool Success, string Message)> UpdateAppointmentAsync(int appointmentId, DateTime date, TimeSpan start, TimeSpan end, int dentistId, int serviceId, string notes)
    {
      try
      {
        // Check for conflicts (excluding current appointment)
        string checkQuery = @"
            SELECT AppointmentID FROM Appointments 
            WHERE DentistID = @DentistID 
            AND CAST(AppointmentDate AS DATE) = @Date
            AND StartTime = @StartTime 
            AND EndTime = @EndTime 
            AND Status NOT IN ('Cancelled', 'No-Show')
            AND AppointmentID != @AppointmentID"; // Exclude current

        var checkParams = new[]
        {
            new SqlParameter("@DentistID", dentistId),
            new SqlParameter("@Date", date.Date),
            new SqlParameter("@StartTime", start),
            new SqlParameter("@EndTime", end),
            new SqlParameter("@AppointmentID", appointmentId)
        };

        DataTable existingAppointment;
        if (IsOffline)
        {
          existingAppointment = await _localDb.GetDataTableAsync(checkQuery, checkParams);
        }
        else
        {
          existingAppointment = await _onlineDb.ExecuteReaderAsync(checkQuery, checkParams);
        }

        if (existingAppointment.Rows.Count > 0)
        {
          return (false, "Time slot already booked.");
        }

        string query = @"
            UPDATE Appointments 
            SET AppointmentDate = @Date, 
                StartTime = @StartTime, 
                EndTime = @EndTime, 
                DentistID = @DentistID, 
                ServiceID = @ServiceID, 
                Notes = @Notes,
                ModifiedDate = @ModifiedDate
            WHERE AppointmentID = @AppointmentID";

        var parameters = new[]
        {
            new SqlParameter("@AppointmentID", appointmentId),
            new SqlParameter("@Date", date),
            new SqlParameter("@StartTime", start),
            new SqlParameter("@EndTime", end),
            new SqlParameter("@DentistID", dentistId),
            new SqlParameter("@ServiceID", serviceId),
            new SqlParameter("@Notes", notes ?? (object)DBNull.Value),
            new SqlParameter("@ModifiedDate", DateTime.Now)
        };

        int rows;
        if (IsOffline)
        {
          rows = await _localDb.ExecuteNonQueryAsync(query, parameters);
          await _localDb.LogChangeAsync("Appointments", appointmentId, "UPDATE");
        }
        else
        {
          rows = await _onlineDb.ExecuteNonQueryAsync(query, parameters);
        }

        return rows > 0 ? (true, "Appointment updated successfully") : (false, "Appointment not found");
      }
      catch (Exception ex)
      {
        return (false, $"Failed to update: {ex.Message}");
      }
    }

    #endregion

    #region Helper Methods

    private Appointment MapAppointmentFromRow(DataRow row)
    {
      var appt = new Appointment
      {
        AppointmentID = Convert.ToInt32(row["AppointmentID"]),
        PatientID = Convert.ToInt32(row["PatientID"]),
        DentistID = row.Table.Columns.Contains("DentistID") && row["DentistID"] != DBNull.Value ? Convert.ToInt32(row["DentistID"]) : null,
        ServiceID = row.Table.Columns.Contains("ServiceID") && row["ServiceID"] != DBNull.Value ? Convert.ToInt32(row["ServiceID"]) : null,
        AppointmentDate = Convert.ToDateTime(row["AppointmentDate"]),
        StartTime = row.Table.Columns.Contains("StartTime") && row["StartTime"] != DBNull.Value ? (TimeSpan)row["StartTime"] : null,
        EndTime = row.Table.Columns.Contains("EndTime") && row["EndTime"] != DBNull.Value ? (TimeSpan)row["EndTime"] : null,
        Status = row.Table.Columns.Contains("Status") && row["Status"] != DBNull.Value ? row["Status"].ToString() ?? string.Empty : string.Empty,
        Notes = row.Table.Columns.Contains("Notes") && row["Notes"] != DBNull.Value ? row["Notes"].ToString() ?? string.Empty : string.Empty,
        CreatedAt = DateTime.Now,
        ServiceName = row.Table.Columns.Contains("ServiceName") && row["ServiceName"] != DBNull.Value ? row["ServiceName"].ToString() ?? string.Empty : string.Empty,
        DentistName = row.Table.Columns.Contains("DentistName") && row["DentistName"] != DBNull.Value ? row["DentistName"].ToString() ?? "TBA" : "TBA",
        PatientName = row.Table.Columns.Contains("PatientName") && row["PatientName"] != DBNull.Value ? row["PatientName"].ToString() ?? string.Empty : string.Empty,
        PatientPhone = row.Table.Columns.Contains("PatientPhone") && row["PatientPhone"] != DBNull.Value ? row["PatientPhone"].ToString() ?? string.Empty : string.Empty,
        PatientEmail = row.Table.Columns.Contains("PatientEmail") && row["PatientEmail"] != DBNull.Value ? row["PatientEmail"].ToString() ?? string.Empty : string.Empty,
      };
      return appt;
    }

    #endregion
  }
}
