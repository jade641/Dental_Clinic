using System.Data;
using Microsoft.Data.SqlClient;

namespace Dental_Clinic.Services
{
    public class SyncService
    {
        private readonly DatabaseService _onlineDb;
     private readonly LocalDatabaseService _localDb;
        private bool _isSyncing = false;

        public event EventHandler<SyncProgressEventArgs> SyncProgressChanged;
        public event EventHandler<SyncCompletedEventArgs> SyncCompleted;

        public SyncService(DatabaseService onlineDb, LocalDatabaseService localDb)
        {
            _onlineDb = onlineDb;
            _localDb = localDb;
        }

        public bool IsSyncing => _isSyncing;

  #region Full Sync (Download from Online to Local)

        public async Task<SyncResult> FullSyncDownloadAsync()
        {
 if (_isSyncing)
            return new SyncResult { Success = false, Message = "Sync already in progress" };

  _isSyncing = true;
var result = new SyncResult();

            try
      {
    // Check online connection
        if (!_onlineDb.IsOnline)
         {
    result.Message = "Cannot sync: No online connection";
 return result;
     }

      ReportProgress("Starting full sync download...", 0);

       // Sync Users
       ReportProgress("Syncing users...", 5);
  await SyncTableDownloadAsync("Users", "UserID");

   // Sync Admin
 ReportProgress("Syncing admin records...", 15);
 await SyncTableDownloadAsync("Admin", "AdminID");

  // Sync Receptionist
   ReportProgress("Syncing receptionist records...", 25);
      await SyncTableDownloadAsync("Receptionist", "ReceptionistID");

        // Sync Dentist
   ReportProgress("Syncing dentist records...", 35);
              await SyncTableDownloadAsync("Dentist", "DentistID");

  // Sync Patient
     ReportProgress("Syncing patient records...", 45);
       await SyncTableDownloadAsync("Patient", "PatientID");

            // Sync ServiceCategory
    ReportProgress("Syncing service categories...", 55);
         await SyncTableDownloadAsync("ServiceCategory", "CategoryID");

  // Sync Services
          ReportProgress("Syncing services...", 70);
            await SyncTableDownloadAsync("Services", "ServiceID");

    // Sync Appointments
       ReportProgress("Syncing appointments...", 85);
 await SyncTableDownloadAsync("Appointments", "AppointmentID");

    ReportProgress("Sync completed successfully!", 100);
        result.Success = true;
     result.Message = "All data synced successfully";
      }
       catch (Exception ex)
 {
           result.Success = false;
          result.Message = $"Sync failed: {ex.Message}";
       ReportProgress($"Sync failed: {ex.Message}", 0);
            }
        finally
      {
     _isSyncing = false;
      SyncCompleted?.Invoke(this, new SyncCompletedEventArgs { Result = result });
            }

    return result;
      }

        private async Task SyncTableDownloadAsync(string tableName, string primaryKeyColumn)
    {
         try
      {
       // Get data from online database
     var onlineData = await _onlineDb.ExecuteReaderAsync($"SELECT * FROM {tableName}");

           if (onlineData == null || onlineData.Rows.Count == 0)
                 return;

         using (var connection = new SqlConnection(_localDb.LocalConnectionString))
    {
          await connection.OpenAsync();

    // Clear existing data (for full sync)
   using (var deleteCmd = new SqlCommand($"DELETE FROM {tableName}", connection))
  {
       await deleteCmd.ExecuteNonQueryAsync();
     }

          // Use SqlDataAdapter to insert data
  var selectCmd = new SqlCommand($"SELECT * FROM {tableName} WHERE 1=0", connection);
        var adapter = new SqlDataAdapter(selectCmd);
    var commandBuilder = new SqlCommandBuilder(adapter);

                 var localTable = new DataTable();
                adapter.Fill(localTable);

       // Copy rows from online to local
     foreach (DataRow row in onlineData.Rows)
       {
var newRow = localTable.NewRow();
    foreach (DataColumn column in onlineData.Columns)
            {
        if (localTable.Columns.Contains(column.ColumnName))
{
                  newRow[column.ColumnName] = row[column.ColumnName];
        }
           }
       
        // Mark as synced
       if (localTable.Columns.Contains("IsSynced"))
              {
           newRow["IsSynced"] = true;
  }
        
  localTable.Rows.Add(newRow);
             }

          // Update database
        adapter.Update(localTable);
       }
            }
        catch (Exception ex)
            {
      System.Diagnostics.Debug.WriteLine($"Error syncing {tableName}: {ex.Message}");
     throw;
            }
        }

   #endregion

  #region Upload Local Changes to Online

  public async Task<SyncResult> SyncUploadPendingChangesAsync()
     {
        if (!_onlineDb.IsOnline)
         return new SyncResult { Success = false, Message = "No online connection" };

            var result = new SyncResult();

            try
            {
          var pendingChanges = await _localDb.GetPendingSyncChangesAsync();

     if (pendingChanges.Rows.Count == 0)
         {
 result.Success = true;
    result.Message = "No pending changes to sync";
 return result;
           }

          ReportProgress($"Uploading {pendingChanges.Rows.Count} pending changes...", 0);

           int processed = 0;
    foreach (DataRow change in pendingChanges.Rows)
       {
            try
           {
   var syncId = Convert.ToInt32(change["SyncID"]);
          var tableName = change["TableName"].ToString();
           var recordId = Convert.ToInt32(change["RecordID"]);
           var operation = change["Operation"].ToString();

await ProcessSyncChange(tableName, recordId, operation);
         await _localDb.MarkSyncedAsync(syncId);

      processed++;
            int progress = (int)((processed / (float)pendingChanges.Rows.Count) * 100);
         ReportProgress($"Uploaded {processed}/{pendingChanges.Rows.Count} changes", progress);
       }
     catch (Exception ex)
              {
                   var syncId = Convert.ToInt32(change["SyncID"]);
              await _localDb.MarkSyncFailedAsync(syncId, ex.Message);
        }
 }

        result.Success = true;
      result.Message = $"Uploaded {processed} changes";
 }
            catch (Exception ex)
            {
       result.Success = false;
   result.Message = $"Upload failed: {ex.Message}";
    }

 return result;
        }

        private async Task ProcessSyncChange(string tableName, int recordId, string operation)
        {
            // Get the record from local database
            var query = $"SELECT * FROM {tableName} WHERE {GetPrimaryKeyColumn(tableName)} = @RecordID";
            var parameters = new[] { new SqlParameter("@RecordID", recordId) };
     var localData = await _localDb.GetDataTableAsync(query, parameters);

            if (localData.Rows.Count == 0)
                return;

      var row = localData.Rows[0];

            switch (operation)
   {
       case "INSERT":
          await InsertToOnlineAsync(tableName, row);
     break;
         case "UPDATE":
            await UpdateOnlineAsync(tableName, row);
        break;
    case "DELETE":
   await DeleteFromOnlineAsync(tableName, recordId);
      break;
   }
        }

        private async Task InsertToOnlineAsync(string tableName, DataRow row)
        {
  var columns = new List<string>();
  var values = new List<string>();
            var sqlParams = new List<SqlParameter>();

            foreach (DataColumn column in row.Table.Columns)
{
                if (column.ColumnName.EndsWith("ID") && column.AutoIncrement)
     continue; // Skip identity columns

             if (column.ColumnName == "IsSynced" || column.ColumnName == "LastModified")
         continue; // Skip sync tracking columns

     columns.Add(column.ColumnName);
      values.Add($"@{column.ColumnName}");
       sqlParams.Add(new SqlParameter($"@{column.ColumnName}", row[column.ColumnName]));
  }

       var insertQuery = $"INSERT INTO {tableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";
        await _onlineDb.ExecuteNonQueryAsync(insertQuery, sqlParams.ToArray());
        }

        private async Task UpdateOnlineAsync(string tableName, DataRow row)
        {
       var setClauses = new List<string>();
         var sqlParams = new List<SqlParameter>();
    var pkColumn = GetPrimaryKeyColumn(tableName);

    foreach (DataColumn column in row.Table.Columns)
   {
 if (column.ColumnName == pkColumn)
       {
             sqlParams.Add(new SqlParameter($"@{column.ColumnName}", row[column.ColumnName]));
        continue; // Skip primary key in SET clause
             }

           if (column.ColumnName == "IsSynced" || column.ColumnName == "LastModified")
            continue; // Skip sync tracking columns

     setClauses.Add($"{column.ColumnName} = @{column.ColumnName}");
         sqlParams.Add(new SqlParameter($"@{column.ColumnName}", row[column.ColumnName]));
    }

        var updateQuery = $"UPDATE {tableName} SET {string.Join(", ", setClauses)} WHERE {pkColumn} = @{pkColumn}";
    await _onlineDb.ExecuteNonQueryAsync(updateQuery, sqlParams.ToArray());
        }

        private async Task DeleteFromOnlineAsync(string tableName, int recordId)
 {
            var pkColumn = GetPrimaryKeyColumn(tableName);
  var deleteQuery = $"DELETE FROM {tableName} WHERE {pkColumn} = @RecordID";
     await _onlineDb.ExecuteNonQueryAsync(deleteQuery, new[] { new SqlParameter("@RecordID", recordId) });
      }

        private string GetPrimaryKeyColumn(string tableName)
        {
   return tableName switch
     {
    "Users" => "UserID",
  "Admin" => "AdminID",
      "Receptionist" => "ReceptionistID",
           "Dentist" => "DentistID",
"Patient" => "PatientID",
            "ServiceCategory" => "CategoryID",
   "Services" => "ServiceID",
  "Appointments" => "AppointmentID",
     _ => throw new ArgumentException($"Unknown table: {tableName}")
    };
  }

  #endregion

        #region Helper Methods

        private void ReportProgress(string message, int percentage)
  {
            SyncProgressChanged?.Invoke(this, new SyncProgressEventArgs
 {
        Message = message,
    Percentage = percentage
            });
   }

        #endregion
    }

    #region Event Args & Result Classes

    public class SyncProgressEventArgs : EventArgs
    {
        public string Message { get; set; }
        public int Percentage { get; set; }
    }

    public class SyncCompletedEventArgs : EventArgs
    {
        public SyncResult Result { get; set; }
    }

    public class SyncResult
    {
 public bool Success { get; set; }
 public string Message { get; set; }
        public DateTime SyncTime { get; set; } = DateTime.Now;
    }

    #endregion
}
