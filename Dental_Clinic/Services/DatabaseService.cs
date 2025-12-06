using System.Data;
using Microsoft.Data.SqlClient;
using Dental_Clinic.Models;
using System.Security.Cryptography;
using System.Text;

namespace Dental_Clinic.Services
{
  public class DatabaseService
  {
    private readonly string _onlineConnectionString;
    private readonly string _offlineConnectionString;
    private bool _isOnline;

    public DatabaseService()
    {

      _onlineConnectionString = "Server=db33114.public.databaseasp.net ;Database=db33114;User Id=db33114;Password=T!t8?w5NdK-7 ;Encrypt=True;TrustServerCertificate=True;Connection Timeout=5;";
      _offlineConnectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=DentalClinicLocal;Integrated Security=True;Connect Timeout=30;Encrypt=False;";
      _isOnline = CheckOnlineStatus();
    }

    public bool RetryConnection()
    {
      _isOnline = CheckOnlineStatus();
      return _isOnline;
    }

    public async Task EnsureDatabaseSchemaAsync()
    {
      try
      {
        using (var conn = GetConnection())
        {
          await conn.OpenAsync();

          // Check if OutstandingBalance column exists in Patient table
          var checkCmd = new SqlCommand(
              "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Patient' AND COLUMN_NAME = 'OutstandingBalance'",
              conn);

          int count = (int)await checkCmd.ExecuteScalarAsync();

          if (count == 0)
          {
            // Add the column if it doesn't exist
            var alterCmd = new SqlCommand(
                "ALTER TABLE Patient ADD OutstandingBalance DECIMAL(18, 2) NOT NULL DEFAULT 0;",
                conn);
            await alterCmd.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine("Schema Updated: Added OutstandingBalance to Patient table.");
          }

          // Check if Payments table exists
          var checkTableCmd = new SqlCommand(
              "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Payments'",
              conn);
          int tableCount = (int)await checkTableCmd.ExecuteScalarAsync();

          if (tableCount == 0)
          {
            var createTableCmd = new SqlCommand(@"
                  CREATE TABLE Payments (
                      PaymentID INT PRIMARY KEY IDENTITY(1,1),
                      PatientID INT NOT NULL,
                      AppointmentID INT NULL, 
                      Amount DECIMAL(10, 2) NOT NULL,
                      PaymentDate DATETIME DEFAULT GETDATE(),
                      PaymentMethod NVARCHAR(50) NOT NULL, 
                      ReferenceNumber NVARCHAR(100) NULL, 
                      Remarks NVARCHAR(255) NULL,
                      FOREIGN KEY (PatientID) REFERENCES Patient(PatientID)
                  );", conn);
            await createTableCmd.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine("Schema Updated: Created Payments table.");
          }

          // Check if Notifications table exists
          var checkNotifTableCmd = new SqlCommand(
              "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Notifications'",
              conn);
          int notifTableCount = (int)await checkNotifTableCmd.ExecuteScalarAsync();

          if (notifTableCount == 0)
          {
            var createNotifTableCmd = new SqlCommand(@"
                  CREATE TABLE Notifications (
                      NotificationID INT PRIMARY KEY IDENTITY(1,1),
                      PatientID INT NOT NULL,
                      Message NVARCHAR(MAX) NOT NULL,
                      DateCreated DATETIME DEFAULT GETDATE(),
                      IsRead BIT DEFAULT 0,
                      Type NVARCHAR(50) NOT NULL,
                      FOREIGN KEY (PatientID) REFERENCES Patient(PatientID)
                  );", conn);
            await createNotifTableCmd.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine("Schema Updated: Created Notifications table.");
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Schema Update Failed: {ex.Message}");
      }
    }

    #region Connection Management

    private bool CheckOnlineStatus()
    {
      try
      {
        using (var connection = new SqlConnection(_onlineConnectionString))
        {
          connection.Open();
          return true;
        }
      }
      catch
      {
        return false;
      }
    }

    private SqlConnection GetConnection()
    {
      _isOnline = CheckOnlineStatus();
      return new SqlConnection(_isOnline ? _onlineConnectionString : _offlineConnectionString);
    }

    public bool IsOnline => _isOnline;

    #endregion

    #region User Authentication

    public async Task<User?> AuthenticateUserAsync(string emailOrUsername, string password)
    {
      using (var connection = GetConnection())
      {
        await connection.OpenAsync();

        string query = @"
 SELECT UserID, RoleName, UserName, Password, FirstName, LastName, PhoneNumber, Age, Sex, Email
           FROM Users
  WHERE (Email = @EmailOrUsername OR UserName = @EmailOrUsername)";

        using (var command = new SqlCommand(query, connection))
        {
          command.Parameters.AddWithValue("@EmailOrUsername", emailOrUsername);

          using (var reader = await command.ExecuteReaderAsync())
          {
            if (await reader.ReadAsync())
            {
              var storedPassword = reader.GetString(3); // Password column
              var inputPassword = password; // Store plain text for now, or hash if your DB stores hashes

              // Check password (plain text comparison - adjust if you hash passwords)
              if (storedPassword == inputPassword)
              {
                var user = new User
                {
                  UserID = reader.GetInt32(0),
                  RoleName = reader.GetString(1),
                  UserName = reader.GetString(2),
                  Password = storedPassword,
                  FirstName = reader.GetString(4),
                  LastName = reader.GetString(5),
                  PhoneNumber = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                  Age = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                  Sex = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                  Email = reader.GetString(9)
                };

                reader.Close();

                // Get role-specific ID
                await LoadRoleSpecificIdAsync(user, connection);

                return user;
              }
            }
          }
        }
      }

      return null;
    }

    private async Task LoadRoleSpecificIdAsync(User user, SqlConnection connection)
    {
      string query = string.Empty;
      string idColumn = string.Empty;

      switch (user.RoleName)
      {
        case "Admin":
          query = "SELECT AdminID FROM Admin WHERE UserID = @UserID";
          idColumn = "AdminID";
          break;
        case "Receptionist":
          query = "SELECT ReceptionistID FROM Receptionist WHERE UserID = @UserID";
          idColumn = "ReceptionistID";
          break;
        case "Dentist":
          query = "SELECT DentistID FROM Dentist WHERE UserID = @UserID";
          idColumn = "DentistID";
          break;
        case "Patient":
          query = "SELECT PatientID FROM Patient WHERE UserID = @UserID";
          idColumn = "PatientID";
          break;
        default:
          return;
      }

      using (var command = new SqlCommand(query, connection))
      {
        command.Parameters.AddWithValue("@UserID", user.UserID);
        var result = await command.ExecuteScalarAsync();

        if (result != null)
        {
          int roleId = Convert.ToInt32(result);
          switch (user.RoleName)
          {
            case "Admin": user.AdminID = roleId; break;
            case "Receptionist": user.ReceptionistID = roleId; break;
            case "Dentist": user.DentistID = roleId; break;
            case "Patient": user.PatientID = roleId; break;
          }
        }
      }
    }

    #endregion

    #region User Registration (Generic)

    public async Task<(bool Success, string Message)> RegisterUserAsync(SignUpModel model)
    {
      // Check if email or username already exists
      if (await EmailOrUsernameExistsAsync(model.Email, model.UserName))
      {
        return (false, "Email or Username already registered");
      }

      using (var connection = GetConnection())
      {
        await connection.OpenAsync();
        using (var transaction = connection.BeginTransaction())
        {
          try
          {
            // Insert into Users table
            string userQuery = @"
       INSERT INTO Users (RoleName, UserName, Password, FirstName, LastName, PhoneNumber, Age, Sex, Email)
    VALUES (@RoleName, @UserName, @Password, @FirstName, @LastName, @PhoneNumber, @Age, @Sex, @Email);
         SELECT CAST(SCOPE_IDENTITY() as int);";

            int userId;
            using (var command = new SqlCommand(userQuery, connection, transaction))
            {
              command.Parameters.AddWithValue("@RoleName", model.Role);
              command.Parameters.AddWithValue("@UserName", model.UserName);
              command.Parameters.AddWithValue("@Password", model.Password); // Store plain text (or hash if needed)
              command.Parameters.AddWithValue("@FirstName", model.FirstName);
              command.Parameters.AddWithValue("@LastName", model.LastName);
              command.Parameters.AddWithValue("@PhoneNumber", model.PhoneNumber ?? (object)DBNull.Value);
              command.Parameters.AddWithValue("@Age", model.Age ?? (object)DBNull.Value);
              command.Parameters.AddWithValue("@Sex", model.Sex ?? (object)DBNull.Value);
              command.Parameters.AddWithValue("@Email", model.Email);

              var result = await command.ExecuteScalarAsync();
              if (result == null || result == DBNull.Value)
              {
                transaction.Rollback();
                return (false, "Failed to create user record.");
              }
              userId = Convert.ToInt32(result);
            }

            // Insert into specific role table
            if (model.Role == "Patient")
            {
              string patientQuery = @"
      INSERT INTO Patient (UserID, BirthDate, Address, MaritalStatus, PhoneNumber, Email, InsuranceProvider, InsurancePolicyNumber, MedicalHistory)
    VALUES (@UserID, @BirthDate, @Address, @MaritalStatus, @PhoneNumber, @Email, @InsuranceProvider, @InsurancePolicyNumber, @MedicalHistory);";

              using (var command = new SqlCommand(patientQuery, connection, transaction))
              {
                command.Parameters.AddWithValue("@UserID", userId);
                command.Parameters.AddWithValue("@BirthDate", model.BirthDate ?? DateTime.Now.AddYears(-25));
                command.Parameters.AddWithValue("@Address", model.Address ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@MaritalStatus", model.MaritalStatus ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@PhoneNumber", model.PhoneNumber ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Email", model.Email);
                command.Parameters.AddWithValue("@InsuranceProvider", model.InsuranceProvider ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@InsurancePolicyNumber", model.InsurancePolicyNumber ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@MedicalHistory", model.MedicalHistory ?? (object)DBNull.Value);

                await command.ExecuteNonQueryAsync();
              }
            }
            else if (model.Role == "Dentist")
            {
              string dentistQuery = @"
      INSERT INTO Dentist (UserID, Specialization, IsAvailable)
    VALUES (@UserID, @Specialization, @IsAvailable);";

              using (var command = new SqlCommand(dentistQuery, connection, transaction))
              {
                command.Parameters.AddWithValue("@UserID", userId);
                command.Parameters.AddWithValue("@Specialization", model.Specialization ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@IsAvailable", model.IsAvailable);
                await command.ExecuteNonQueryAsync();
              }
            }
            else if (model.Role == "Receptionist")
            {
              string recepQuery = "INSERT INTO Receptionist (UserID) VALUES (@UserID)";
              using (var command = new SqlCommand(recepQuery, connection, transaction))
              {
                command.Parameters.AddWithValue("@UserID", userId);
                await command.ExecuteNonQueryAsync();
              }
            }
            else if (model.Role == "Admin")
            {
              string adminQuery = "INSERT INTO Admin (UserID) VALUES (@UserID)";
              using (var command = new SqlCommand(adminQuery, connection, transaction))
              {
                command.Parameters.AddWithValue("@UserID", userId);
                await command.ExecuteNonQueryAsync();
              }
            }

            transaction.Commit();
            return (true, "User created successfully!");
          }
          catch (SqlException ex)
          {
            transaction.Rollback();
            return (false, $"Database error: {ex.Message}");
          }
        }
      }
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
      var users = new List<User>();
      using (var connection = GetConnection())
      {
        await connection.OpenAsync();
        // Check if IsActive column exists, if not, we might need to handle it or alter table. 
        // For now, I'll assume we can select it or default it. 
        // To be safe against schema mismatch, I'll try to select it, but if it fails, I'll default to true.
        // Actually, let's just try to select it. If the user wants "soft delete", the column must exist.
        // I will add a check/alter in InitializeDatabaseAsync for offline, but for online I assume it's there or I'll add it.

        string query = "SELECT UserID, RoleName, UserName, Password, FirstName, LastName, PhoneNumber, Age, Sex, Email, IsActive FROM Users ORDER BY UserID DESC";

        try
        {
          using (var command = new SqlCommand(query, connection))
          using (var reader = await command.ExecuteReaderAsync())
          {
            while (await reader.ReadAsync())
            {
              users.Add(new User
              {
                UserID = reader.GetInt32(0),
                RoleName = reader.GetString(1),
                UserName = reader.GetString(2),
                Password = reader.GetString(3),
                FirstName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                LastName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                PhoneNumber = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Age = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                Sex = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Email = reader.GetString(9),
                IsActive = !reader.IsDBNull(10) && reader.GetBoolean(10)
              });
            }
          }
        }
        catch (SqlException)
        {
          // Fallback if IsActive column doesn't exist yet
          query = "SELECT UserID, RoleName, UserName, Password, FirstName, LastName, PhoneNumber, Age, Sex, Email FROM Users ORDER BY UserID DESC";
          using (var command = new SqlCommand(query, connection))
          using (var reader = await command.ExecuteReaderAsync())
          {
            while (await reader.ReadAsync())
            {
              users.Add(new User
              {
                UserID = reader.GetInt32(0),
                RoleName = reader.GetString(1),
                UserName = reader.GetString(2),
                Password = reader.GetString(3),
                FirstName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                LastName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                PhoneNumber = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Age = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                Sex = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Email = reader.GetString(9),
                IsActive = true // Default to true
              });
            }
          }
        }
      }
      return users;
    }

    public async Task<(bool Success, string Message)> UpdateUserAsync(SignUpModel model, int userId)
    {
      using (var connection = GetConnection())
      {
        await connection.OpenAsync();
        using (var transaction = connection.BeginTransaction())
        {
          try
          {
            // Update Users table
            var updateFields = new List<string>
                    {
                        "FirstName = @FirstName",
                        "LastName = @LastName",
                        "PhoneNumber = @PhoneNumber",
                        "Age = @Age",
                        "Sex = @Sex",
                        "Email = @Email"
                    };

            if (!string.IsNullOrEmpty(model.Password))
            {
              updateFields.Add("Password = @Password");
            }

            string userQuery = $@"
                        UPDATE Users 
                        SET {string.Join(", ", updateFields)}
                        WHERE UserID = @UserID";

            using (var command = new SqlCommand(userQuery, connection, transaction))
            {
              command.Parameters.AddWithValue("@UserID", userId);
              command.Parameters.AddWithValue("@FirstName", model.FirstName);
              command.Parameters.AddWithValue("@LastName", model.LastName);
              command.Parameters.AddWithValue("@PhoneNumber", model.PhoneNumber ?? (object)DBNull.Value);
              command.Parameters.AddWithValue("@Age", model.Age ?? (object)DBNull.Value);
              command.Parameters.AddWithValue("@Sex", model.Sex ?? (object)DBNull.Value);
              command.Parameters.AddWithValue("@Email", model.Email);

              if (!string.IsNullOrEmpty(model.Password))
              {
                command.Parameters.AddWithValue("@Password", model.Password);
              }

              await command.ExecuteNonQueryAsync();
            }

            // Update specific role table
            if (model.Role == "Patient")
            {
              string patientQuery = @"
                            UPDATE Patient 
                            SET BirthDate = @BirthDate, 
                                Address = @Address, 
                                MaritalStatus = @MaritalStatus, 
                                PhoneNumber = @PhoneNumber, 
                                Email = @Email, 
                                InsuranceProvider = @InsuranceProvider, 
                                InsurancePolicyNumber = @InsurancePolicyNumber, 
                                MedicalHistory = @MedicalHistory
                            WHERE UserID = @UserID";

              using (var command = new SqlCommand(patientQuery, connection, transaction))
              {
                command.Parameters.AddWithValue("@UserID", userId);
                command.Parameters.AddWithValue("@BirthDate", model.BirthDate ?? DateTime.Now);
                command.Parameters.AddWithValue("@Address", model.Address ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@MaritalStatus", model.MaritalStatus ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@PhoneNumber", model.PhoneNumber ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@Email", model.Email);
                command.Parameters.AddWithValue("@InsuranceProvider", model.InsuranceProvider ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@InsurancePolicyNumber", model.InsurancePolicyNumber ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@MedicalHistory", model.MedicalHistory ?? (object)DBNull.Value);
                await command.ExecuteNonQueryAsync();
              }
            }
            else if (model.Role == "Dentist")
            {
              string dentistQuery = @"
                            UPDATE Dentist 
                            SET Specialization = @Specialization, 
                                IsAvailable = @IsAvailable
                            WHERE UserID = @UserID";

              using (var command = new SqlCommand(dentistQuery, connection, transaction))
              {
                command.Parameters.AddWithValue("@UserID", userId);
                command.Parameters.AddWithValue("@Specialization", model.Specialization ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@IsAvailable", model.IsAvailable);
                await command.ExecuteNonQueryAsync();
              }
            }

            transaction.Commit();
            return (true, "User updated successfully!");
          }
          catch (Exception ex)
          {
            transaction.Rollback();
            return (false, $"Update failed: {ex.Message}");
          }
        }
      }
    }

    public async Task<(bool Success, string Message)> ToggleUserStatusAsync(int userId, bool isActive)
    {
      using (var connection = GetConnection())
      {
        await connection.OpenAsync();
        try
        {
          // Ensure column exists (simple check/add for this operation if needed, or just try update)
          // For simplicity, we assume the column exists or we catch the error.
          // If it doesn't exist, we might want to add it.

          string checkColQuery = "SELECT COL_LENGTH('Users', 'IsActive')";
          using (var cmd = new SqlCommand(checkColQuery, connection))
          {
            var len = await cmd.ExecuteScalarAsync();
            if (len == DBNull.Value)
            {
              // Column doesn't exist, add it
              string addColQuery = "ALTER TABLE Users ADD IsActive BIT DEFAULT 1 WITH VALUES";
              using (var addCmd = new SqlCommand(addColQuery, connection))
              {
                await addCmd.ExecuteNonQueryAsync();
              }
            }
          }

          string query = "UPDATE Users SET IsActive = @IsActive WHERE UserID = @UserID";
          using (var command = new SqlCommand(query, connection))
          {
            command.Parameters.AddWithValue("@IsActive", isActive);
            command.Parameters.AddWithValue("@UserID", userId);
            await command.ExecuteNonQueryAsync();
          }
          return (true, $"User status updated to {(isActive ? "Active" : "Inactive")}");
        }
        catch (Exception ex)
        {
          return (false, $"Failed to update status: {ex.Message}");
        }
      }
    }

    private async Task<bool> EmailOrUsernameExistsAsync(string email, string username)
    {
      using (var connection = GetConnection())
      {
        await connection.OpenAsync();

        string query = "SELECT COUNT(*) FROM Users WHERE Email = @Email OR UserName = @UserName";
        using (var command = new SqlCommand(query, connection))
        {
          command.Parameters.AddWithValue("@Email", email);
          command.Parameters.AddWithValue("@UserName", username);
          var result = await command.ExecuteScalarAsync();
          int count = result is int i ? i : Convert.ToInt32(result ?? 0);
          return count > 0;
        }
      }
    }

    #endregion

    #region Staff Authentication

    public async Task<User?> AuthenticateStaffAsync(string emailOrUsername, string password, string accessLevel)
    {
      var user = await AuthenticateUserAsync(emailOrUsername, password);

      if (user != null)
      {
        // Validate role matches access level
        bool isAuthorized = accessLevel switch
        {
          "admin" => user.RoleName == "Admin",
          "receptionist" => user.RoleName == "Receptionist",
          "staff" => user.RoleName == "Dentist",
          _ => false
        };

        return isAuthorized ? user : null;
      }

      return null;
    }

    #endregion

    #region Database Initialization

    public async Task InitializeDatabaseAsync()
    {
      // Only initialize if using offline database
      if (_isOnline)
        return;

      using (var connection = GetConnection())
      {
        await connection.OpenAsync();

        // Check if Users table exists
        string checkQuery = @"
       IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
           SELECT 0
        ELSE
   SELECT 1";

        using (var command = new SqlCommand(checkQuery, connection))
        {
          var result = await command.ExecuteScalarAsync();
          int exists = result is int i ? i : Convert.ToInt32(result ?? 0);
          if (exists == 1)
            return; // Tables already exist
        }

        // Create all tables for offline mode (simplified version)
        string createTablesQuery = @"
   -- Create Users table
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
IsActive BIT DEFAULT 1
     );

  -- Create Admin table
   CREATE TABLE Admin (
        AdminID INT PRIMARY KEY IDENTITY(1,1),
               UserID INT NOT NULL,
CONSTRAINT FK_Admin_Users FOREIGN KEY (UserID) REFERENCES Users(UserID)
       );

-- Create Receptionist table
   CREATE TABLE Receptionist (
  ReceptionistID INT PRIMARY KEY IDENTITY(1,1),
    UserID INT NOT NULL,
      CONSTRAINT FK_Receptionist_Users FOREIGN KEY (UserID) REFERENCES Users(UserID)
   );

         -- Create Dentist table
    CREATE TABLE Dentist (
       DentistID INT PRIMARY KEY IDENTITY(1,1),
           UserID INT NOT NULL,
  Specialization NVARCHAR(100),
  IsAvailable BIT,
     ProfileImg NVARCHAR(MAX),
           CONSTRAINT FK_Dentist_Users FOREIGN KEY (UserID) REFERENCES Users(UserID)
                );

        -- Create Patient table
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
     CONSTRAINT FK_Patient_Users FOREIGN KEY (UserID) REFERENCES Users(UserID)
);

      -- Insert default admin
     INSERT INTO Users (RoleName, UserName, Password, FirstName, LastName, Email, PhoneNumber)
VALUES ('Admin', 'admin', 'admin123', 'Admin', 'User', 'admin@dentalclinic.com', '+639123456789');

       DECLARE @AdminUserID INT = SCOPE_IDENTITY();
       INSERT INTO Admin (UserID) VALUES (@AdminUserID);";

        using (var command = new SqlCommand(createTablesQuery, connection))
        {
          await command.ExecuteNonQueryAsync();
        }
      }
    }

    #endregion

    #region Password Reset

    public async Task<(bool Success, string Message)> ResetPasswordAsync(string email, string token, string newPassword)
    {
      using (var connection = GetConnection())
      {
        await connection.OpenAsync();
        using (var transaction = connection.BeginTransaction())
        {
          try
          {
            // Verify token
            var verifyQuery = @"
          SELECT Id, ExpiryTime, IsUsed 
            FROM PasswordResetTokens 
        WHERE Email = @Email AND Token = @Token";

            using (var command = new SqlCommand(verifyQuery, connection, transaction))
            {
              command.Parameters.AddWithValue("@Email", email);
              command.Parameters.AddWithValue("@Token", token);

              using (var reader = await command.ExecuteReaderAsync())
              {
                if (!await reader.ReadAsync())
                {
                  return (false, "Invalid or expired reset token.");
                }

                var expiryTime = reader.GetDateTime(1);
                var isUsed = reader.GetBoolean(2);

                if (isUsed)
                {
                  return (false, "This reset link has already been used.");
                }

                if (DateTime.UtcNow > expiryTime)
                {
                  return (false, "This reset link has expired. Please request a new one.");
                }
              }
            }

            // Update password
            var updateQuery = @"
       UPDATE Users 
      SET Password = @NewPassword 
       WHERE Email = @Email";

            using (var command = new SqlCommand(updateQuery, connection, transaction))
            {
              command.Parameters.AddWithValue("@NewPassword", newPassword); // In production, hash this
              command.Parameters.AddWithValue("@Email", email);
              await command.ExecuteNonQueryAsync();
            }

            // Mark token as used
            var markUsedQuery = @"
      UPDATE PasswordResetTokens 
     SET IsUsed = 1 
          WHERE Email = @Email AND Token = @Token";

            using (var command = new SqlCommand(markUsedQuery, connection, transaction))
            {
              command.Parameters.AddWithValue("@Email", email);
              command.Parameters.AddWithValue("@Token", token);
              await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
            return (true, "Password reset successful!");
          }
          catch (Exception ex)
          {
            transaction.Rollback();
            return (false, $"Error resetting password: {ex.Message}");
          }
        }
      }
    }

    #endregion

    #region Data Operations for Sync

    public async Task<DataTable> ExecuteReaderAsync(string query, SqlParameter[] parameters = null)
    {
      using (var connection = new SqlConnection(_onlineConnectionString))
      {
        await connection.OpenAsync();
        using (var command = new SqlCommand(query, connection))
        {
          if (parameters != null)
          {
            command.Parameters.AddRange(parameters);
          }

          using (var reader = await command.ExecuteReaderAsync())
          {
            var dataTable = new DataTable();
            dataTable.Load(reader);
            return dataTable;
          }
        }
      }
    }

    public async Task<int> ExecuteNonQueryAsync(string query, SqlParameter[] parameters = null)
    {
      using (var connection = new SqlConnection(_onlineConnectionString))
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

    public async Task<object> ExecuteScalarAsync(string query, SqlParameter[] parameters = null)
    {
      using (var connection = new SqlConnection(_onlineConnectionString))
      {
        await connection.OpenAsync();
        using (var command = new SqlCommand(query, connection))
        {
          if (parameters != null)
          {
            command.Parameters.AddRange(parameters);
          }

          var result = await command.ExecuteScalarAsync();
          // Fix CS8603: Ensure a non-null return value
          return result ?? new object();
        }
      }
    }

    #endregion

    #region Sync Methods

    public async Task<bool> SyncLocalToOnlineAsync()
    {
      if (!_isOnline) return false;

      try
      {
        // TODO: Implement sync logic for offline changes
        // This is a placeholder for future implementation
        await Task.Yield(); // Ensures the method is truly asynchronous
        return true;
      }
      catch
      {
        return false;
      }
    }

    #endregion
    #region Billing Methods

    public async Task<decimal> GetPatientBalanceAsync(int patientId)
    {
      string query = "SELECT OutstandingBalance FROM Patient WHERE PatientID = @PatientID";
      using (var connection = GetConnection())
      {
        await connection.OpenAsync();
        using (var command = new SqlCommand(query, connection))
        {
          command.Parameters.AddWithValue("@PatientID", patientId);
          var result = await command.ExecuteScalarAsync();
          if (result != null && result != DBNull.Value)
          {
            return Convert.ToDecimal(result);
          }
          return 0;
        }
      }
    }

    public async Task<(bool Success, string Message)> DeductPatientBalanceAsync(int patientId, decimal amount)
    {
      try
      {
        string query = "UPDATE Patient SET OutstandingBalance = OutstandingBalance - @Amount WHERE PatientID = @PatientID";
        using (var connection = GetConnection())
        {
          await connection.OpenAsync();
          using (var command = new SqlCommand(query, connection))
          {
            command.Parameters.AddWithValue("@Amount", amount);
            command.Parameters.AddWithValue("@PatientID", patientId);
            int rows = await command.ExecuteNonQueryAsync();
            return rows > 0 ? (true, "Balance updated") : (false, "Patient not found");
          }
        }
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    public async Task<bool> UpdateAppointmentCostAndTreatmentAsync(int appointmentId, int patientId, decimal finalCost, string treatmentNotes, string diagnosis)
    {
      try
      {
        using (var connection = GetConnection())
        {
          await connection.OpenAsync();

          // 0. Guard Clause: Check if already completed to prevent double billing
          string checkStatusQuery = "SELECT Status FROM Appointments WHERE AppointmentID = @AppointmentID";
          using (var checkCmd = new SqlCommand(checkStatusQuery, connection))
          {
            checkCmd.Parameters.AddWithValue("@AppointmentID", appointmentId);
            var status = (string)await checkCmd.ExecuteScalarAsync();
            if (status == "Completed")
            {
              System.Diagnostics.Debug.WriteLine($"[DatabaseService] Blocked duplicate transaction for Appointment {appointmentId}. Already Completed.");
              return false;
            }
          }

          using (var transaction = connection.BeginTransaction())
          {
            try
            {
              // 1. Get the original service cost and details for this appointment
              decimal originalCost = 0;
              int serviceId = 0;
              int dentistId = 0;

              string getDetailsQuery = @"
                            SELECT s.Cost, a.ServiceID, a.DentistID
                            FROM Appointments a
                            JOIN Services s ON a.ServiceID = s.ServiceID
                            WHERE a.AppointmentID = @AppointmentID";

              using (var cmd = new SqlCommand(getDetailsQuery, connection, transaction))
              {
                cmd.Parameters.AddWithValue("@AppointmentID", appointmentId);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                  if (await reader.ReadAsync())
                  {
                    originalCost = reader.GetDecimal(0);
                    serviceId = reader.GetInt32(1);
                    dentistId = reader.GetInt32(2);
                  }
                }
              }

              // 2. Record Service Transaction (This is the official record of the fee)
              string insertTransactionQuery = @"
                  INSERT INTO ServiceTransactions (AppointmentID, PatientID, DentistID, ServiceID, TransactionDate, Quantity, Cost, TotalAmount, Status)
                  VALUES (@AppointmentID, @PatientID, @DentistID, @ServiceID, GETDATE(), 1, @Cost, @TotalAmount, 'Completed')";

              using (var cmd = new SqlCommand(insertTransactionQuery, connection, transaction))
              {
                cmd.Parameters.AddWithValue("@AppointmentID", appointmentId);
                cmd.Parameters.AddWithValue("@PatientID", patientId);
                cmd.Parameters.AddWithValue("@DentistID", dentistId);
                cmd.Parameters.AddWithValue("@ServiceID", serviceId);
                cmd.Parameters.AddWithValue("@Cost", finalCost); // The final agreed cost
                cmd.Parameters.AddWithValue("@TotalAmount", finalCost);
                await cmd.ExecuteNonQueryAsync();
              }

              // 3. Update Patient Balance (Add the FINAL cost to their balance)
              // Note: We add the full final cost because "Service First, Pay Later" means they haven't paid yet.
              // If they had paid a deposit, we would subtract that, but assuming standard flow:
              string updateBalanceQuery = @"
                                UPDATE Patient 
                                SET OutstandingBalance = OutstandingBalance + @FinalCost 
                                WHERE PatientID = @PatientID";

              using (var cmd = new SqlCommand(updateBalanceQuery, connection, transaction))
              {
                cmd.Parameters.AddWithValue("@FinalCost", finalCost);
                cmd.Parameters.AddWithValue("@PatientID", patientId);
                await cmd.ExecuteNonQueryAsync();
              }

              // 4. Record Treatment Details
              string insertTreatmentQuery = @"
                            INSERT INTO TreatmentRecord (AppointmentID, PatientID, DentistID, Diagnosis, TreatmentNotes, DateRecorded)
                            VALUES (@AppointmentID, @PatientID, @DentistID, @Diagnosis, @TreatmentNotes, GETDATE())";

              using (var cmd = new SqlCommand(insertTreatmentQuery, connection, transaction))
              {
                cmd.Parameters.AddWithValue("@AppointmentID", appointmentId);
                cmd.Parameters.AddWithValue("@PatientID", patientId);
                cmd.Parameters.AddWithValue("@DentistID", dentistId);
                cmd.Parameters.AddWithValue("@Diagnosis", diagnosis ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@TreatmentNotes", treatmentNotes ?? (object)DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
              }

              // 5. Update Appointment Status to Completed
              string updateStatusQuery = "UPDATE Appointments SET Status = 'Completed' WHERE AppointmentID = @AppointmentID";
              using (var cmd = new SqlCommand(updateStatusQuery, connection, transaction))
              {
                cmd.Parameters.AddWithValue("@AppointmentID", appointmentId);
                await cmd.ExecuteNonQueryAsync();
              }

              // 6. Add Notification
              string notificationQuery = @"INSERT INTO Notifications (PatientID, Message, Type) VALUES (@PatientID, @Message, 'Billing')";
              using (var cmd = new SqlCommand(notificationQuery, connection, transaction))
              {
                cmd.Parameters.AddWithValue("@PatientID", patientId);
                cmd.Parameters.AddWithValue("@Message", $"A new service fee of ₱{finalCost:N0} has been added to your account for your recent appointment.");
                await cmd.ExecuteNonQueryAsync();
              }

              transaction.Commit();
              return true;
            }
            catch (Exception)
            {
              transaction.Rollback();
              throw;
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"UpdateAppointmentCost Failed: {ex.Message}");
        return false;
      }
    }

    public async Task<decimal> GetServiceBaseCostAsync(int serviceId)
    {
      try
      {
        string query = "SELECT Cost FROM Services WHERE ServiceID = @ServiceID";
        using (var conn = GetConnection())
        {
          await conn.OpenAsync();
          using (var cmd = new SqlCommand(query, conn))
          {
            cmd.Parameters.AddWithValue("@ServiceID", serviceId);
            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value ? Convert.ToDecimal(result) : 0;
          }
        }
      }
      catch { return 0; }
    }

    public async Task<bool> HasPastDueBalanceAsync(int patientId)
    {
      try
      {
        // Simple check: If OutstandingBalance > 0, they have a past due balance.
        // You could refine this to check for "aged" debt if needed.
        decimal balance = await GetPatientBalanceAsync(patientId);
        return balance > 0;
      }
      catch { return false; }
    }

    public async Task<bool> RecordPaymentAsync(PaymentTransaction payment)
    {
      try
      {
        string query = @"
                INSERT INTO Payments (PatientID, AppointmentID, Amount, PaymentDate, PaymentMethod, ReferenceNumber, Remarks)
                VALUES (@PatientID, @AppointmentID, @Amount, @PaymentDate, @PaymentMethod, @ReferenceNumber, @Remarks)";

        using (var connection = GetConnection())
        {
          await connection.OpenAsync();
          using (var command = new SqlCommand(query, connection))
          {
            command.Parameters.AddWithValue("@PatientID", payment.PatientID);
            command.Parameters.AddWithValue("@AppointmentID", (object?)payment.AppointmentID ?? DBNull.Value);
            command.Parameters.AddWithValue("@Amount", payment.Amount);
            command.Parameters.AddWithValue("@PaymentDate", payment.PaymentDate);
            command.Parameters.AddWithValue("@PaymentMethod", payment.PaymentMethod);
            command.Parameters.AddWithValue("@ReferenceNumber", (object?)payment.ReferenceNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@Remarks", (object?)payment.Remarks ?? DBNull.Value);

            await command.ExecuteNonQueryAsync();
            return true;
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"RecordPayment Failed: {ex.Message}");
        return false;
      }
    }

    public async Task<List<PaymentTransaction>> GetPaymentsByPatientIdAsync(int patientId)
    {
      var payments = new List<PaymentTransaction>();
      try
      {
        string query = "SELECT * FROM Payments WHERE PatientID = @PatientID ORDER BY PaymentDate DESC";
        using (var connection = GetConnection())
        {
          await connection.OpenAsync();
          using (var command = new SqlCommand(query, connection))
          {
            command.Parameters.AddWithValue("@PatientID", patientId);
            using (var reader = await command.ExecuteReaderAsync())
            {
              while (await reader.ReadAsync())
              {
                payments.Add(new PaymentTransaction
                {
                  PaymentID = reader.GetInt32(reader.GetOrdinal("PaymentID")),
                  PatientID = reader.GetInt32(reader.GetOrdinal("PatientID")),
                  AppointmentID = reader.IsDBNull(reader.GetOrdinal("AppointmentID")) ? null : reader.GetInt32(reader.GetOrdinal("AppointmentID")),
                  Amount = reader.GetDecimal(reader.GetOrdinal("Amount")),
                  PaymentDate = reader.GetDateTime(reader.GetOrdinal("PaymentDate")),
                  PaymentMethod = reader.GetString(reader.GetOrdinal("PaymentMethod")),
                  ReferenceNumber = reader.IsDBNull(reader.GetOrdinal("ReferenceNumber")) ? null : reader.GetString(reader.GetOrdinal("ReferenceNumber")),
                  Remarks = reader.IsDBNull(reader.GetOrdinal("Remarks")) ? null : reader.GetString(reader.GetOrdinal("Remarks"))
                });
              }
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"GetPayments Failed: {ex.Message}");
      }
      return payments;
    }

    #endregion

    #region Notifications

    public async Task AddNotificationAsync(int patientId, string message, string type)
    {
      try
      {
        string query = @"INSERT INTO Notifications (PatientID, Message, Type) VALUES (@PatientID, @Message, @Type)";
        using (var connection = GetConnection())
        {
          await connection.OpenAsync();
          using (var command = new SqlCommand(query, connection))
          {
            command.Parameters.AddWithValue("@PatientID", patientId);
            command.Parameters.AddWithValue("@Message", message);
            command.Parameters.AddWithValue("@Type", type);
            await command.ExecuteNonQueryAsync();
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error adding notification: {ex.Message}");
      }
    }

    public async Task<List<Notification>> GetNotificationsAsync(int patientId)
    {
      var notifications = new List<Notification>();
      try
      {
        string query = "SELECT * FROM Notifications WHERE PatientID = @PatientID ORDER BY DateCreated DESC";
        using (var connection = GetConnection())
        {
          await connection.OpenAsync();
          using (var command = new SqlCommand(query, connection))
          {
            command.Parameters.AddWithValue("@PatientID", patientId);
            using (var reader = await command.ExecuteReaderAsync())
            {
              while (await reader.ReadAsync())
              {
                notifications.Add(new Notification
                {
                  NotificationID = reader.GetInt32(reader.GetOrdinal("NotificationID")),
                  PatientID = reader.GetInt32(reader.GetOrdinal("PatientID")),
                  Message = reader.GetString(reader.GetOrdinal("Message")),
                  DateCreated = reader.GetDateTime(reader.GetOrdinal("DateCreated")),
                  IsRead = reader.GetBoolean(reader.GetOrdinal("IsRead")),
                  Type = reader.GetString(reader.GetOrdinal("Type"))
                });
              }
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error fetching notifications: {ex.Message}");
      }
      return notifications;
    }

    public async Task MarkNotificationAsReadAsync(int notificationId)
    {
      try
      {
        string query = "UPDATE Notifications SET IsRead = 1 WHERE NotificationID = @NotificationID";
        using (var connection = GetConnection())
        {
          await connection.OpenAsync();
          using (var command = new SqlCommand(query, connection))
          {
            command.Parameters.AddWithValue("@NotificationID", notificationId);
            await command.ExecuteNonQueryAsync();
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error marking notification as read: {ex.Message}");
      }
    }

    #endregion

  }
}
