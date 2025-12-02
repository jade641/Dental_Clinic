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

    #region User Registration (Patient)

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
              command.Parameters.AddWithValue("@RoleName", "Patient");
              command.Parameters.AddWithValue("@UserName", model.UserName);
              command.Parameters.AddWithValue("@Password", model.Password); // Store plain text (or hash if needed)
              command.Parameters.AddWithValue("@FirstName", model.FirstName);
              command.Parameters.AddWithValue("@LastName", model.LastName);
              command.Parameters.AddWithValue("@PhoneNumber", model.PhoneNumber ?? (object)DBNull.Value);
              command.Parameters.AddWithValue("@Age", model.Age ?? (object)DBNull.Value);
              command.Parameters.AddWithValue("@Sex", model.Sex ?? (object)DBNull.Value);
              command.Parameters.AddWithValue("@Email", model.Email);

              // userId = (int)await command.ExecuteScalarAsync();
              var result = await command.ExecuteScalarAsync();
              if (result == null || result == DBNull.Value)
              {
                transaction.Rollback();
                return (false, "Failed to create user record.");
              }
              userId = Convert.ToInt32(result);
            }

            // Insert into Patient table
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

            transaction.Commit();
            return (true, "Registration successful!");
          }
          catch (SqlException ex)
          {
            transaction.Rollback();
            return (false, $"Database error: {ex.Message}");
          }
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
Email NVARCHAR(100) NOT NULL
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
  }
}
