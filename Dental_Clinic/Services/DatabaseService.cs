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

          int count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

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
          int tableCount = Convert.ToInt32(await checkTableCmd.ExecuteScalarAsync());

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
          int notifTableCount = Convert.ToInt32(await checkNotifTableCmd.ExecuteScalarAsync());

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

          // Check if ImageUrl column exists in MarketingCampaign table
          var checkCampaignColCmd = new SqlCommand(
              "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MarketingCampaign' AND COLUMN_NAME = 'ImageUrl'",
              conn);

          int campaignColCount = Convert.ToInt32(await checkCampaignColCmd.ExecuteScalarAsync());

          if (campaignColCount == 0)
          {
            // Check if table exists first
            var checkTableExistsCmd = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MarketingCampaign'", conn);
            if (Convert.ToInt32(await checkTableExistsCmd.ExecuteScalarAsync()) > 0)
            {
              var alterCmd = new SqlCommand(
                  "ALTER TABLE MarketingCampaign ADD ImageUrl NVARCHAR(MAX) NULL;",
                  conn);
              await alterCmd.ExecuteNonQueryAsync();
              System.Diagnostics.Debug.WriteLine("Schema Updated: Added ImageUrl to MarketingCampaign table.");
            }
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

    public async Task<DataTable> ExecuteReaderAsync(string query, SqlParameter[]? parameters = null)
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

    public async Task<int> ExecuteNonQueryAsync(string query, SqlParameter[]? parameters = null)
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

    public async Task<object> ExecuteScalarAsync(string query, SqlParameter[]? parameters = null)
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
            var status = await checkCmd.ExecuteScalarAsync() as string;
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

    #region Feedback

    public async Task<bool> AddFeedbackAsync(Feedback feedback)
    {
      try
      {
        string query = @"
                INSERT INTO Feedback (PatientID, AppointmentID, SubmissionDate, RatingValue, FeedbackText)
                VALUES (@PatientID, @AppointmentID, @SubmissionDate, @RatingValue, @FeedbackText)";

        using (var conn = GetConnection())
        {
          await conn.OpenAsync();
          using (var cmd = new SqlCommand(query, conn))
          {
            cmd.Parameters.AddWithValue("@PatientID", feedback.PatientID);
            cmd.Parameters.AddWithValue("@AppointmentID", (object)feedback.AppointmentID ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SubmissionDate", feedback.SubmissionDate);
            cmd.Parameters.AddWithValue("@RatingValue", (object)feedback.RatingValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FeedbackText", (object)feedback.FeedbackText ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
          }
        }
        return true;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[DatabaseService] Error adding feedback: {ex.Message}");
        return false;
      }
    }

    public async Task<List<Appointment>> GetUnratedCompletedAppointmentsAsync(int patientId)
    {
      var appointments = new List<Appointment>();
      try
      {
        // Select completed appointments that don't have a corresponding entry in the Feedback table
        // Note: Dentist table has UserID, we might need to join Users to get the name if DentistName isn't stored directly or if we want the name from Users table.
        // Assuming Dentist table has a way to get name, or we join with Users.
        // The schema shows Dentist table has UserID. Users table has FirstName, LastName.

        string query = @"
                SELECT a.AppointmentID, a.AppointmentDate, a.StartTime, a.EndTime, s.ServiceName, 
                       u.FirstName + ' ' + u.LastName as DentistName
                FROM Appointments a
                JOIN Services s ON a.ServiceID = s.ServiceID
                JOIN Dentist d ON a.DentistID = d.DentistID
                JOIN Users u ON d.UserID = u.UserID
                LEFT JOIN Feedback f ON a.AppointmentID = f.AppointmentID
                WHERE a.PatientID = @PatientID 
                  AND a.Status = 'Completed' 
                  AND f.FeedbackID IS NULL
                ORDER BY a.AppointmentDate DESC";

        using (var conn = GetConnection())
        {
          await conn.OpenAsync();
          using (var cmd = new SqlCommand(query, conn))
          {
            cmd.Parameters.AddWithValue("@PatientID", patientId);
            using (var reader = await cmd.ExecuteReaderAsync())
            {
              while (await reader.ReadAsync())
              {
                appointments.Add(new Appointment
                {
                  AppointmentID = reader.GetInt32(reader.GetOrdinal("AppointmentID")),
                  AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                  StartTime = reader.GetTimeSpan(reader.GetOrdinal("StartTime")),
                  EndTime = reader.GetTimeSpan(reader.GetOrdinal("EndTime")),
                  ServiceName = reader.IsDBNull(reader.GetOrdinal("ServiceName")) ? string.Empty : reader.GetString(reader.GetOrdinal("ServiceName")),
                  DentistName = reader.IsDBNull(reader.GetOrdinal("DentistName")) ? "Unknown" : reader.GetString(reader.GetOrdinal("DentistName"))
                });
              }
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[DatabaseService] Error getting unrated appointments: {ex.Message}");
      }
      return appointments;
    }

    #endregion

    #region Marketing

    public async Task<List<FeedbackDisplayModel>> GetAllFeedbackAsync()
    {
      var list = new List<FeedbackDisplayModel>();
      try
      {
        // Join Feedback, Patient, Users to get names
        string query = @"
                SELECT f.FeedbackID, f.PatientID, f.RatingValue, f.FeedbackText, f.SubmissionDate,
                       u.FirstName, u.LastName, u.Email
                FROM Feedback f
                JOIN Patient p ON f.PatientID = p.PatientID
                JOIN Users u ON p.UserID = u.UserID
                ORDER BY f.SubmissionDate DESC";

        using (var conn = GetConnection())
        {
          await conn.OpenAsync();
          using (var cmd = new SqlCommand(query, conn))
          {
            using (var reader = await cmd.ExecuteReaderAsync())
            {
              while (await reader.ReadAsync())
              {
                var fname = reader.GetString(reader.GetOrdinal("FirstName"));
                var lname = reader.GetString(reader.GetOrdinal("LastName"));
                list.Add(new FeedbackDisplayModel
                {
                  FeedbackID = reader.GetInt32(reader.GetOrdinal("FeedbackID")),
                  PatientID = reader.GetInt32(reader.GetOrdinal("PatientID")),
                  PatientName = $"{fname} {lname}",
                  PatientEmail = reader.GetString(reader.GetOrdinal("Email")),
                  Rating = reader.IsDBNull(reader.GetOrdinal("RatingValue")) ? 0 : reader.GetInt32(reader.GetOrdinal("RatingValue")),
                  FeedbackText = reader.IsDBNull(reader.GetOrdinal("FeedbackText")) ? "" : reader.GetString(reader.GetOrdinal("FeedbackText")),
                  Date = reader.GetDateTime(reader.GetOrdinal("SubmissionDate")),
                  IsReplied = false // Not tracking replies in DB yet
                });
              }
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[DatabaseService] Error getting feedback: {ex.Message}");
      }
      return list;
    }

    #region Analytics

    public async Task<decimal> GetTotalRevenueAsync()
    {
        try
        {
            string query = "SELECT SUM(Amount) FROM Payments";
            using (var conn = GetConnection())
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    var result = await cmd.ExecuteScalarAsync();
                    return result != DBNull.Value ? Convert.ToDecimal(result) : 0;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting total revenue: {ex.Message}");
            return 0;
        }
    }

    public async Task<int> GetTotalPatientsAsync()
    {
        try
        {
            string query = "SELECT COUNT(*) FROM Patient";
            using (var conn = GetConnection())
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    return (int)await cmd.ExecuteScalarAsync();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting total patients: {ex.Message}");
            return 0;
        }
    }

    public async Task<int> GetTotalAppointmentsAsync()
    {
        try
        {
            string query = "SELECT COUNT(*) FROM Appointments";
            using (var conn = GetConnection())
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    return (int)await cmd.ExecuteScalarAsync();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting total appointments: {ex.Message}");
            return 0;
        }
    }

    public async Task<Dictionary<string, decimal>> GetMonthlyRevenueAsync(int months)
    {
        var data = new Dictionary<string, decimal>();
        try
        {
            string query = @"
                SELECT FORMAT(PaymentDate, 'MMM') as Month, SUM(Amount) as Total
                FROM Payments
                WHERE PaymentDate >= DATEADD(month, -@Months, GETDATE())
                GROUP BY FORMAT(PaymentDate, 'MMM'), MONTH(PaymentDate)
                ORDER BY MONTH(PaymentDate)";

            using (var conn = GetConnection())
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Months", months);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(reader.GetString(0), reader.GetDecimal(1));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting monthly revenue: {ex.Message}");
        }
        return data;
    }

    public async Task<Dictionary<string, int>> GetServiceDistributionAsync()
    {
        var data = new Dictionary<string, int>();
        try
        {
            string query = @"
                SELECT s.ServiceName, COUNT(a.AppointmentID) as Count
                FROM Appointments a
                JOIN Services s ON a.ServiceID = s.ServiceID
                GROUP BY s.ServiceName
                ORDER BY Count DESC";

            using (var conn = GetConnection())
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(reader.GetString(0), reader.GetInt32(1));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting service distribution: {ex.Message}");
        }
        return data;
    }

    public async Task<Dictionary<string, decimal>> GetWeeklyRevenueAsync(int weeks)
    {
        var data = new Dictionary<string, decimal>();
        try
        {
            // Group by week start date (Monday)
            string query = @"
                SET DATEFIRST 1;
                SELECT 
                    FORMAT(DATEADD(day, 1 - DATEPART(weekday, PaymentDate), CAST(PaymentDate AS DATE)), 'MM/dd') as WeekStart,
                    SUM(Amount) as Total
                FROM Payments
                WHERE PaymentDate >= DATEADD(week, -@Weeks, GETDATE())
                GROUP BY DATEADD(day, 1 - DATEPART(weekday, PaymentDate), CAST(PaymentDate AS DATE))
                ORDER BY DATEADD(day, 1 - DATEPART(weekday, PaymentDate), CAST(PaymentDate AS DATE))";

            using (var conn = GetConnection())
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Weeks", weeks);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(reader.GetString(0), reader.GetDecimal(1));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting weekly revenue: {ex.Message}");
        }
        return data;
    }

    public async Task<List<(string Service, decimal Amount, int Percentage)>> GetServiceRevenueAsync(int days)
    {
        var list = new List<(string Service, decimal Amount, int Percentage)>();
        try
        {
            decimal totalRevenue = 0;
            
            // First get total revenue for percentage calculation
            string totalQuery = "SELECT SUM(Amount) FROM Payments WHERE PaymentDate >= DATEADD(day, -@Days, GETDATE())";
            
            // Then get breakdown
            string query = @"
                SELECT TOP 5 s.ServiceName, SUM(p.Amount) as TotalRevenue
                FROM Payments p
                JOIN Appointments a ON p.AppointmentID = a.AppointmentID
                JOIN Services s ON a.ServiceID = s.ServiceID
                WHERE p.PaymentDate >= DATEADD(day, -@Days, GETDATE())
                GROUP BY s.ServiceName
                ORDER BY TotalRevenue DESC";

            using (var conn = GetConnection())
            {
                await conn.OpenAsync();
                
                using (var cmdTotal = new SqlCommand(totalQuery, conn))
                {
                    cmdTotal.Parameters.AddWithValue("@Days", days);
                    var result = await cmdTotal.ExecuteScalarAsync();
                    if (result != DBNull.Value)
                        totalRevenue = Convert.ToDecimal(result);
                }

                if (totalRevenue > 0)
                {
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Days", days);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string service = reader.GetString(0);
                                decimal amount = reader.GetDecimal(1);
                                int percentage = (int)((amount / totalRevenue) * 100);
                                list.Add((service, amount, percentage));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting service revenue: {ex.Message}");
        }
        return list;
    }

    public async Task<(List<string> Labels, List<int> Patients, List<decimal> Revenue)> GetDailyPerformanceAsync(int days)
    {
        var labels = new List<string>();
        var patients = new List<int>();
        var revenue = new List<decimal>();

        try
        {
            // We need a continuous date range, so we'll generate it in C# or use a recursive CTE
            // For simplicity, let's fetch data and merge in C#
            
            var revenueData = new Dictionary<DateTime, decimal>();
            var patientData = new Dictionary<DateTime, int>();

            string revQuery = @"
                SELECT CAST(PaymentDate as Date) as Date, SUM(Amount) as Revenue
                FROM Payments
                WHERE PaymentDate >= DATEADD(day, -@Days, GETDATE())
                GROUP BY CAST(PaymentDate as Date)";

            string patQuery = @"
                SELECT CAST(AppointmentDate as Date), COUNT(*) as Count
                FROM Appointments
                WHERE AppointmentDate >= DATEADD(day, -@Days, GETDATE())
                GROUP BY CAST(AppointmentDate as Date)";

            using (var conn = GetConnection())
            {
                await conn.OpenAsync();

                using (var cmd = new SqlCommand(revQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Days", days);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            revenueData[reader.GetDateTime(0)] = reader.GetDecimal(1);
                        }
                    }
                }

                using (var cmd = new SqlCommand(patQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Days", days);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            patientData[reader.GetDateTime(0)] = reader.GetInt32(1);
                        }
                    }
                }
            }

            // Merge and fill gaps
            for (int i = days - 1; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                labels.Add(date.ToString("MMM dd"));
                revenue.Add(revenueData.ContainsKey(date) ? revenueData[date] : 0);
                patients.Add(patientData.ContainsKey(date) ? patientData[date] : 0);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting daily performance: {ex.Message}");
        }

        return (labels, patients, revenue);
    }

    #endregion

    public async Task<List<PatientEmailModel>> GetAllPatientsWithEmailAsync()
    {
      var list = new List<PatientEmailModel>();
      try
      {
        string query = @"
                SELECT p.PatientID, u.FirstName, u.LastName, u.Email
                FROM Patient p
                JOIN Users u ON p.UserID = u.UserID
                WHERE u.Email IS NOT NULL AND u.Email <> ''";

        using (var conn = GetConnection())
        {
          await conn.OpenAsync();
          using (var cmd = new SqlCommand(query, conn))
          {
            using (var reader = await cmd.ExecuteReaderAsync())
            {
              while (await reader.ReadAsync())
              {
                var fname = reader.GetString(reader.GetOrdinal("FirstName"));
                var lname = reader.GetString(reader.GetOrdinal("LastName"));
                list.Add(new PatientEmailModel
                {
                  PatientID = reader.GetInt32(reader.GetOrdinal("PatientID")),
                  Name = $"{fname} {lname}",
                  Email = reader.GetString(reader.GetOrdinal("Email"))
                });
              }
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[DatabaseService] Error getting patients: {ex.Message}");
      }
      return list;
    }

    public async Task<List<PatientEmailModel>> GetPatientsByRatingAsync(int minRating)
    {
      var list = new List<PatientEmailModel>();
      try
      {
        string query = @"
                SELECT DISTINCT p.PatientID, u.FirstName, u.LastName, u.Email
                FROM Patient p
                JOIN Users u ON p.UserID = u.UserID
                JOIN Feedback f ON p.PatientID = f.PatientID
                WHERE f.Rating >= @MinRating AND u.Email IS NOT NULL AND u.Email != ''";

        using (var conn = GetConnection())
        {
          await conn.OpenAsync();
          using (var cmd = new SqlCommand(query, conn))
          {
            cmd.Parameters.AddWithValue("@MinRating", minRating);
            using (var reader = await cmd.ExecuteReaderAsync())
            {
              while (await reader.ReadAsync())
              {
                list.Add(new PatientEmailModel
                {
                  PatientID = reader.GetInt32(0),
                  Name = $"{reader.GetString(1)} {reader.GetString(2)}",
                  Email = reader.GetString(3)
                });
              }
            }
          }
        }
      }
      catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
      return list;
    }

    public async Task<List<PatientEmailModel>> GetRecentPatientsAsync(int days)
    {
      var list = new List<PatientEmailModel>();
      try
      {
        string query = @"
                SELECT DISTINCT p.PatientID, u.FirstName, u.LastName, u.Email
                FROM Patient p
                JOIN Users u ON p.UserID = u.UserID
                JOIN Appointment a ON p.PatientID = a.PatientID
                WHERE a.AppointmentDate >= DATEADD(day, -@Days, GETDATE()) AND u.Email IS NOT NULL AND u.Email != ''";

        using (var conn = GetConnection())
        {
          await conn.OpenAsync();
          using (var cmd = new SqlCommand(query, conn))
          {
            cmd.Parameters.AddWithValue("@Days", days);
            using (var reader = await cmd.ExecuteReaderAsync())
            {
              while (await reader.ReadAsync())
              {
                list.Add(new PatientEmailModel
                {
                  PatientID = reader.GetInt32(0),
                  Name = $"{reader.GetString(1)} {reader.GetString(2)}",
                  Email = reader.GetString(3)
                });
              }
            }
          }
        }
      }
      catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
      return list;
    }

    public async Task<List<PatientEmailModel>> GetInactivePatientsAsync(int months)
    {
      var list = new List<PatientEmailModel>();
      try
      {
        string query = @"
                SELECT p.PatientID, u.FirstName, u.LastName, u.Email
                FROM Patient p
                JOIN Users u ON p.UserID = u.UserID
                WHERE u.Email IS NOT NULL AND u.Email != ''
                AND p.PatientID NOT IN (
                    SELECT PatientID FROM Appointment 
                    WHERE AppointmentDate >= DATEADD(month, -@Months, GETDATE())
                )";

        using (var conn = GetConnection())
        {
          await conn.OpenAsync();
          using (var cmd = new SqlCommand(query, conn))
          {
            cmd.Parameters.AddWithValue("@Months", months);
            using (var reader = await cmd.ExecuteReaderAsync())
            {
              while (await reader.ReadAsync())
              {
                list.Add(new PatientEmailModel
                {
                  PatientID = reader.GetInt32(0),
                  Name = $"{reader.GetString(1)} {reader.GetString(2)}",
                  Email = reader.GetString(3)
                });
              }
            }
          }
        }
      }
      catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}"); }
      return list;
    }

    public async Task<int> CreateMarketingCampaignAsync(string name, string template, DateTime end, string imageUrl = "")
    {
      try
      {
        string query = @"
                INSERT INTO MarketingCampaign (CampaignName, StartDate, EndDate, ContextTemplate, ImageUrl)
                VALUES (@Name, @Start, @End, @Template, @ImageUrl);
                SELECT CAST(SCOPE_IDENTITY() as int);";

        using (var conn = GetConnection())
        {
          await conn.OpenAsync();
          using (var cmd = new SqlCommand(query, conn))
          {
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Start", DateTime.Now);
            cmd.Parameters.AddWithValue("@End", end);
            cmd.Parameters.AddWithValue("@Template", template);
            cmd.Parameters.AddWithValue("@ImageUrl", (object)imageUrl ?? DBNull.Value);

            var result = await cmd.ExecuteScalarAsync();
            return result != null ? Convert.ToInt32(result) : 0;
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[DatabaseService] Error creating campaign: {ex.Message}");
        return 0;
      }
    }

    public async Task RecordCampaignRecipientAsync(int campaignId, int patientId)
    {
      try
      {
        string query = @"
                INSERT INTO CampaignRecipient (CampaignID, PatientID, SentTime, Status)
                VALUES (@CampaignID, @PatientID, @SentTime, 'Sent')";

        using (var conn = GetConnection())
        {
          await conn.OpenAsync();
          using (var cmd = new SqlCommand(query, conn))
          {
            cmd.Parameters.AddWithValue("@CampaignID", campaignId);
            cmd.Parameters.AddWithValue("@PatientID", patientId);
            cmd.Parameters.AddWithValue("@SentTime", DateTime.Now);
            await cmd.ExecuteNonQueryAsync();
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[DatabaseService] Error recording recipient: {ex.Message}");
      }
    }

    public async Task<List<CampaignModel>> GetCampaignsAsync()
    {
      var list = new List<CampaignModel>();
      try
      {
        // Get campaigns and count recipients
        string query = @"
                SELECT c.CampaignID, c.CampaignName, c.ContextTemplate, c.StartDate, c.EndDate,
                       (SELECT COUNT(*) FROM CampaignRecipient cr WHERE cr.CampaignID = c.CampaignID) as SentCount
                FROM MarketingCampaign c
                ORDER BY c.StartDate DESC";

        using (var conn = GetConnection())
        {
          await conn.OpenAsync();
          using (var cmd = new SqlCommand(query, conn))
          {
            using (var reader = await cmd.ExecuteReaderAsync())
            {
              while (await reader.ReadAsync())
              {
                list.Add(new CampaignModel
                {
                  CampaignID = reader.GetInt32(reader.GetOrdinal("CampaignID")),
                  CampaignName = reader.GetString(reader.GetOrdinal("CampaignName")),
                  Description = reader.IsDBNull(reader.GetOrdinal("ContextTemplate")) ? "" : reader.GetString(reader.GetOrdinal("ContextTemplate")),
                  StartDate = reader.IsDBNull(reader.GetOrdinal("StartDate")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("StartDate")),
                  EndDate = reader.GetDateTime(reader.GetOrdinal("EndDate")),
                  SentCount = reader.GetInt32(reader.GetOrdinal("SentCount"))
                });
              }
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[DatabaseService] Error getting campaigns: {ex.Message}");
      }
      return list;
    }

    #endregion

  }
}
