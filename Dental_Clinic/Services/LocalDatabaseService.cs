using System.Data;
using Microsoft.Data.SqlClient;

namespace Dental_Clinic.Services
{
    public class LocalDatabaseService
    {
      private readonly string _localConnectionString;
        private readonly string _dbPath;

        public LocalDatabaseService()
        {
   // Get app data directory for storing local database
          var appDataPath = FileSystem.AppDataDirectory;
     _dbPath = Path.Combine(appDataPath, "DentalClinicLocal.mdf");
            
            // Connection string for LocalDB or SQLite
   _localConnectionString = $"Data Source=(LocalDB)\\MSSQLLocalDB;AttachDbFilename={_dbPath};Integrated Security=True;Connect Timeout=30";
        }

  public string LocalConnectionString => _localConnectionString;
        public string DatabasePath => _dbPath;

        #region Database Initialization

      public async Task<bool> InitializeLocalDatabaseAsync()
        {
 try
            {
      // Check if database file already exists
    if (File.Exists(_dbPath))
 {
     return true; // Already initialized
      }

      // Create database file and tables
        await CreateDatabaseFileAsync();
     await CreateTablesAsync();
         await InsertDefaultDataAsync();

        return true;
     }
            catch (Exception ex)
            {
             System.Diagnostics.Debug.WriteLine($"Database initialization error: {ex.Message}");
      return false;
            }
        }

    private async Task CreateDatabaseFileAsync()
        {
   // Create empty database file
     var masterConnectionString = "Data Source=(LocalDB)\\MSSQLLocalDB;Integrated Security=True;Connect Timeout=30";
   
         using (var connection = new SqlConnection(masterConnectionString))
            {
        await connection.OpenAsync();
   
              var createDbCommand = $@"
        IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'DentalClinicLocal')
    BEGIN
     CREATE DATABASE DentalClinicLocal
   ON PRIMARY (NAME = DentalClinicLocal_Data, FILENAME = '{_dbPath}')
      END";

                using (var command = new SqlCommand(createDbCommand, connection))
                {
        await command.ExecuteNonQueryAsync();
      }
  }
}

      private async Task CreateTablesAsync()
        {
            using (var connection = new SqlConnection(_localConnectionString))
            {
       await connection.OpenAsync();

     var createTablesScript = @"
          -- Users Table
      IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
          BEGIN
   CREATE TABLE Users (
   UserID INT PRIMARY KEY IDENTITY(1,1),
      RoleName NVARCHAR(50) NOT NULL,
 UserName NVARCHAR(100) NOT NULL UNIQUE,
            Password NVARCHAR(100) NOT NULL,
       FirstName NVARCHAR(100),
       LastName NVARCHAR(100),
      PhoneNumber NVARCHAR(50),
      Age INT,
        Sex NVARCHAR(20),
       Email NVARCHAR(100) NOT NULL,
      IsSynced BIT DEFAULT 0,
 LastModified DATETIME DEFAULT GETDATE()
     );
  END

        -- Admin Table
  IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Admin')
      BEGIN
           CREATE TABLE Admin (
    AdminID INT PRIMARY KEY IDENTITY(1,1),
   UserID INT NOT NULL,
          IsSynced BIT DEFAULT 0,
    CONSTRAINT FK_Admin_Users FOREIGN KEY (UserID) REFERENCES Users(UserID)
  );
         END

      -- Receptionist Table
       IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Receptionist')
        BEGIN
            CREATE TABLE Receptionist (
  ReceptionistID INT PRIMARY KEY IDENTITY(1,1),
   UserID INT NOT NULL,
        IsSynced BIT DEFAULT 0,
CONSTRAINT FK_Receptionist_Users FOREIGN KEY (UserID) REFERENCES Users(UserID)
              );
 END

    -- Dentist Table
     IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Dentist')
     BEGIN
              CREATE TABLE Dentist (
 DentistID INT PRIMARY KEY IDENTITY(1,1),
       UserID INT NOT NULL,
    Specialization NVARCHAR(100),
  IsAvailable BIT,
        ProfileImg NVARCHAR(MAX),
    IsSynced BIT DEFAULT 0,
CONSTRAINT FK_Dentist_Users FOREIGN KEY (UserID) REFERENCES Users(UserID)
     );
          END

        -- Patient Table
     IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Patient')
        BEGIN
             CREATE TABLE Patient (
          PatientID INT PRIMARY KEY IDENTITY(1,1),
      UserID INT NOT NULL,
    MedicalAlerts NVARCHAR(500),
      BirthDate DATE NOT NULL,
    Address NVARCHAR(500),
   MaritalStatus NVARCHAR(50),
       ProfileImg NVARCHAR(MAX),
     MedicalHistory NVARCHAR(MAX),
  PhoneNumber NVARCHAR(50),
 Email NVARCHAR(100),
      InsuranceProvider NVARCHAR(100),
InsurancePolicyNumber NVARCHAR(100),
            IsSynced BIT DEFAULT 0,
 CONSTRAINT FK_Patient_Users FOREIGN KEY (UserID) REFERENCES Users(UserID)
   );
 END

           -- ServiceCategory Table
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ServiceCategory')
 BEGIN
     CREATE TABLE ServiceCategory (
           CategoryID INT PRIMARY KEY IDENTITY(1,1),
   CategoryName NVARCHAR(100) NOT NULL,
      Description NVARCHAR(500),
        IsSynced BIT DEFAULT 0
         );
       END

     -- Services Table
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Services')
        BEGIN
 CREATE TABLE Services (
 ServiceID INT PRIMARY KEY IDENTITY(1,1),
  CategoryID INT,
    ServiceName NVARCHAR(200) NOT NULL,
      Description NVARCHAR(500),
     Duration INT NOT NULL,
      IsActive BIT DEFAULT 1,
  IsSynced BIT DEFAULT 0,
     CONSTRAINT FK_Services_Category FOREIGN KEY (CategoryID) REFERENCES ServiceCategory(CategoryID)
 );
       END

       -- Appointments Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Appointments')
    BEGIN
           CREATE TABLE Appointments (
       AppointmentID INT PRIMARY KEY IDENTITY(1,1),
        PatientID INT NOT NULL,
   DentistID INT,
     ServiceID INT,
     EventID INT,
     AppointmentDate DATE NOT NULL,
  StartTime TIME,
        EndTime TIME,
     Status NVARCHAR(50) DEFAULT 'Scheduled',
     Notes NVARCHAR(MAX),
     CreatedAt DATETIME DEFAULT GETDATE(),
     CanceledBy INT,
            CancelationReason NVARCHAR(500),
            NoShowReason NVARCHAR(500),
         ModifiedAt DATETIME,
         IsSynced BIT DEFAULT 0,
    CONSTRAINT FK_Appointments_Patient FOREIGN KEY (PatientID) REFERENCES Patient(PatientID),
     CONSTRAINT FK_Appointments_Dentist FOREIGN KEY (DentistID) REFERENCES Dentist(DentistID),
      CONSTRAINT FK_Appointments_Service FOREIGN KEY (ServiceID) REFERENCES Services(ServiceID)
   );
 END

           -- Sync Log Table (track what needs to be synced)
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SyncLog')
    BEGIN
CREATE TABLE SyncLog (
       SyncID INT PRIMARY KEY IDENTITY(1,1),
            TableName NVARCHAR(100) NOT NULL,
    RecordID INT NOT NULL,
    Operation NVARCHAR(20) NOT NULL, -- INSERT, UPDATE, DELETE
    SyncStatus NVARCHAR(20) DEFAULT 'Pending', -- Pending, Completed, Failed
        CreatedAt DATETIME DEFAULT GETDATE(),
    SyncedAt DATETIME NULL,
   ErrorMessage NVARCHAR(MAX) NULL
      );
         END";

      using (var command = new SqlCommand(createTablesScript, connection))
   {
         await command.ExecuteNonQueryAsync();
         }
        }
        }

        private async Task InsertDefaultDataAsync()
        {
     using (var connection = new SqlConnection(_localConnectionString))
   {
  await connection.OpenAsync();

      // Insert default offline admin user
             var insertDefaultUser = @"
         IF NOT EXISTS (SELECT 1 FROM Users WHERE UserName = 'offline_admin')
 BEGIN
       INSERT INTO Users (RoleName, UserName, Password, FirstName, LastName, Email, IsSynced)
         VALUES ('Admin', 'offline_admin', 'admin123', 'Offline', 'Admin', 'offline@local.com', 0);

       DECLARE @UserID INT = SCOPE_IDENTITY();
  
          INSERT INTO Admin (UserID, IsSynced)
         VALUES (@UserID, 0);
   END";

                using (var command = new SqlCommand(insertDefaultUser, connection))
       {
  await command.ExecuteNonQueryAsync();
      }
    }
}

        #endregion

        #region Data Operations with SqlDataAdapter

        public async Task<DataTable> GetDataTableAsync(string query, SqlParameter[] parameters = null)
        {
 using (var connection = new SqlConnection(_localConnectionString))
            {
           using (var adapter = new SqlDataAdapter(query, connection))
          {
     if (parameters != null)
           {
   adapter.SelectCommand.Parameters.AddRange(parameters);
        }

              var dataTable = new DataTable();
          await Task.Run(() => adapter.Fill(dataTable));
           return dataTable;
     }
            }
        }

        public async Task<int> ExecuteNonQueryAsync(string query, SqlParameter[] parameters = null)
        {
 using (var connection = new SqlConnection(_localConnectionString))
            {
      await connection.OpenAsync();
      using (var command = new SqlCommand(query, connection))
       {
       if (parameters != null)
         {
     command.Parameters.AddRange(parameters);
 }

     return await command.ExecuteNonQueryAsync();
    }
            }
  }

        public async Task<object?> ExecuteScalarAsync(string query, SqlParameter[] parameters = null)
 {
        using (var connection = new SqlConnection(_localConnectionString))
         {
          await connection.OpenAsync();
    using (var command = new SqlCommand(query, connection))
        {
          if (parameters != null)
       {
        command.Parameters.AddRange(parameters);
          }

            // The return value of ExecuteScalarAsync can be null, so use nullable object
    return await command.ExecuteScalarAsync();
             }
            }
        }

        #endregion

      #region Sync Log Operations

  public async Task LogChangeAsync(string tableName, int recordId, string operation)
      {
            var query = @"
                INSERT INTO SyncLog (TableName, RecordID, Operation, SyncStatus)
    VALUES (@TableName, @RecordID, @Operation, 'Pending')";

       var parameters = new[]
          {
    new SqlParameter("@TableName", tableName),
       new SqlParameter("@RecordID", recordId),
                new SqlParameter("@Operation", operation)
            };

            await ExecuteNonQueryAsync(query, parameters);
        }

        public async Task<DataTable> GetPendingSyncChangesAsync()
        {
            var query = @"
         SELECT * FROM SyncLog 
      WHERE SyncStatus = 'Pending' 
                ORDER BY CreatedAt ASC";

            return await GetDataTableAsync(query);
        }

   public async Task MarkSyncedAsync(int syncId)
        {
        var query = @"
              UPDATE SyncLog 
         SET SyncStatus = 'Completed', SyncedAt = GETDATE() 
      WHERE SyncID = @SyncID";

     await ExecuteNonQueryAsync(query, new[] { new SqlParameter("@SyncID", syncId) });
        }

    public async Task MarkSyncFailedAsync(int syncId, string errorMessage)
   {
 var query = @"
     UPDATE SyncLog 
   SET SyncStatus = 'Failed', ErrorMessage = @ErrorMessage 
      WHERE SyncID = @SyncID";

          var parameters = new[]
            {
   new SqlParameter("@SyncID", syncId),
  new SqlParameter("@ErrorMessage", errorMessage)
    };

            await ExecuteNonQueryAsync(query, parameters);
      }

        #endregion

        #region Database Health Check

public async Task<bool> CheckDatabaseHealthAsync()
        {
            try
            {
         using (var connection = new SqlConnection(_localConnectionString))
      {
   await connection.OpenAsync();
         return connection.State == ConnectionState.Open;
              }
 }
        catch
            {
   return false;
            }
     }

        #endregion
    }
}
