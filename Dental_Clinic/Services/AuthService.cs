using Dental_Clinic.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace Dental_Clinic.Services
{
    public class AuthService
    {
        private readonly DatabaseService _onlineDb;
        private readonly LocalDatabaseService _localDb;
        private bool _useOfflineMode;

        public AuthService(DatabaseService onlineDb, LocalDatabaseService localDb)
        {
            _onlineDb = onlineDb;
            _localDb = localDb;
            _useOfflineMode = false;
        }

        public bool IsOfflineMode => _useOfflineMode;
        public bool IsOnline => _onlineDb.IsOnline;

        public async Task SwitchToOfflineModeAsync()
        {
            _useOfflineMode = true;
            await _localDb.InitializeLocalDatabaseAsync();
        }

        public void SwitchToOnlineMode()
        {
            _useOfflineMode = false;
        }

        public async Task<UserSession?> LoginAsync(LoginModel model)
        {
            if (_useOfflineMode || !_onlineDb.IsOnline)
            {
                // Use local database
                return await LoginOfflineAsync(model.EmailOrUsername, model.Password);
            }
            else
            {
                // Use online database
                var user = await _onlineDb.AuthenticateUserAsync(model.EmailOrUsername, model.Password);

                if (user != null)
                {
                    return new UserSession
                    {
                        UserId = user.UserID,
                        UserName = user.FullName,
                        Role = user.RoleName,
                        Token = GenerateToken(user),
                        RoleSpecificId = GetRoleSpecificId(user)
                    };
                }
            }

            return null;
        }

        public async Task<UserSession?> StaffLoginAsync(StaffLoginModel model)
        {
            if (_useOfflineMode || !_onlineDb.IsOnline)
            {
                // Use local database
                return await StaffLoginOfflineAsync(model.EmailOrUsername, model.Password, model.AccessLevel);
            }
            else
            {
                // Use online database
                var user = await _onlineDb.AuthenticateStaffAsync(model.EmailOrUsername, model.Password, model.AccessLevel);

                if (user != null)
                {
                    return new UserSession
                    {
                        UserId = user.UserID,
                        UserName = user.FullName,
                        Role = user.RoleName,
                        Token = GenerateToken(user),
                        RoleSpecificId = GetRoleSpecificId(user)
                    };
                }
            }

            return null;
        }

        private async Task<UserSession?> LoginOfflineAsync(string emailOrUsername, string password)
        {
            var query = @"
      SELECT u.*, 
      a.AdminID, 
  r.ReceptionistID, 
       d.DentistID, 
      p.PatientID
       FROM Users u
          LEFT JOIN Admin a ON u.UserID = a.UserID
   LEFT JOIN Receptionist r ON u.UserID = r.UserID
    LEFT JOIN Dentist d ON u.UserID = d.UserID
   LEFT JOIN Patient p ON u.UserID = p.UserID
       WHERE (u.Email = @EmailOrUsername OR u.UserName = @EmailOrUsername) 
AND u.Password = @Password";

            var parameters = new[]
            {
                new SqlParameter("@EmailOrUsername", emailOrUsername),
                new SqlParameter("@Password", password)
            };

            var result = await _localDb.GetDataTableAsync(query, parameters);

            if (result.Rows.Count > 0)
            {
                var row = result.Rows[0];
                return new UserSession
                {
                    UserId = Convert.ToInt32(row["UserID"]),
                    UserName = $"{row["FirstName"]} {row["LastName"]}",
                    Role = row["RoleName"].ToString(),
                    Token = GenerateOfflineToken(row),
                    RoleSpecificId = GetRoleSpecificIdFromRow(row)
                };
            }

            return null;
        }

        private async Task<UserSession?> StaffLoginOfflineAsync(string emailOrUsername, string password, string accessLevel)
        {
            var roleName = accessLevel switch
            {
                "admin" => "Admin",
                "receptionist" => "Receptionist",
                "staff" => "Dentist",
                _ => "Dentist"
            };

            var query = @"
                SELECT u.*, 
     a.AdminID, 
  r.ReceptionistID, 
     d.DentistID
         FROM Users u
    LEFT JOIN Admin a ON u.UserID = a.UserID
   LEFT JOIN Receptionist r ON u.UserID = r.UserID
    LEFT JOIN Dentist d ON u.UserID = d.UserID
      WHERE (u.Email = @EmailOrUsername OR u.UserName = @EmailOrUsername) 
 AND u.Password = @Password 
   AND u.RoleName = @RoleName";

            var parameters = new[]
            {
                new SqlParameter("@EmailOrUsername", emailOrUsername),
                new SqlParameter("@Password", password),
                new SqlParameter("@RoleName", roleName)
            };

            var result = await _localDb.GetDataTableAsync(query, parameters);

            if (result.Rows.Count > 0)
            {
                var row = result.Rows[0];
                return new UserSession
                {
                    UserId = Convert.ToInt32(row["UserID"]),
                    UserName = $"{row["FirstName"]} {row["LastName"]}",
                    Role = row["RoleName"].ToString(),
                    Token = GenerateOfflineToken(row),
                    RoleSpecificId = GetRoleSpecificIdFromRow(row)
                };
            }

            return null;
        }

        public async Task<(bool Success, string Message)> RegisterAsync(SignUpModel model)
        {
            if (_useOfflineMode || !_onlineDb.IsOnline)
            {
                return await RegisterOfflineAsync(model);
            }
            else
            {
                return await _onlineDb.RegisterUserAsync(model);
            }
        }

        private async Task<(bool Success, string Message)> RegisterOfflineAsync(SignUpModel model)
        {
            try
            {
                // Check if user already exists
                var checkQuery = "SELECT COUNT(*) FROM Users WHERE Email = @Email OR UserName = @UserName";
                var checkParams = new[]
                {
                    new SqlParameter("@Email", model.Email),
                    new SqlParameter("@UserName", model.UserName)
                };

                var existingCount = Convert.ToInt32(await _localDb.ExecuteScalarAsync(checkQuery, checkParams));
                if (existingCount > 0)
                {
                    return (false, "User already exists");
                }

                // Insert user
                var insertQuery = @"
          INSERT INTO Users (RoleName, UserName, Password, FirstName, LastName, PhoneNumber, Email, IsSynced)
          OUTPUT INSERTED.UserID
          VALUES (@RoleName, @UserName, @Password, @FirstName, @LastName, @PhoneNumber, @Email, 0)";

                var insertParams = new[]
                {
                    new SqlParameter("@RoleName", "Patient"),
                    new SqlParameter("@UserName", model.UserName),
                    new SqlParameter("@Password", model.Password),
                    new SqlParameter("@FirstName", model.FirstName),
                    new SqlParameter("@LastName", model.LastName),
                    new SqlParameter("@PhoneNumber", model.PhoneNumber ?? string.Empty),
                    new SqlParameter("@Email", model.Email)
                };

                var userId = Convert.ToInt32(await _localDb.ExecuteScalarAsync(insertQuery, insertParams));

                // Insert patient record
                var patientQuery = @"
          INSERT INTO Patient (UserID, PhoneNumber, Email, IsSynced, BirthDate)
                OUTPUT INSERTED.PatientID
             VALUES (@UserID, @PhoneNumber, @Email, 0, @BirthDate)";

                var patientParams = new[]
                {
                    new SqlParameter("@UserID", userId),
                    new SqlParameter("@PhoneNumber", model.PhoneNumber ?? string.Empty),
                    new SqlParameter("@Email", model.Email),
                    new SqlParameter("@BirthDate", DateTime.Now.Date)
                };

                var patientId = Convert.ToInt32(await _localDb.ExecuteScalarAsync(patientQuery, patientParams));

                // Log for sync
                await _localDb.LogChangeAsync("Users", userId, "INSERT");
                await _localDb.LogChangeAsync("Patient", patientId, "INSERT");

                return (true, "Registration successful (offline mode - will sync when online)");
            }
            catch (Exception ex)
            {
                return (false, $"Registration failed: {ex.Message}");
            }
        }

        private string GenerateToken(User user)
        {
            // Simple token generation - in production, use JWT
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user.UserID}:{user.Email}:{DateTime.UtcNow.Ticks}"));
        }

        private string GenerateOfflineToken(DataRow row)
        {
            var userId = row["UserID"];
            var email = row["Email"];
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{userId}:{email}:{DateTime.UtcNow.Ticks}:offline"));
        }

        private int? GetRoleSpecificId(User user)
        {
            return user.RoleName switch
            {
                "Admin" => user.AdminID,
                "Receptionist" => user.ReceptionistID,
                "Dentist" => user.DentistID,
                "Patient" => user.PatientID,
                _ => null
            };
        }

        private int? GetRoleSpecificIdFromRow(DataRow row)
        {
            var roleName = row["RoleName"].ToString();
            return roleName switch
            {
                "Admin" => row["AdminID"] != DBNull.Value ? Convert.ToInt32(row["AdminID"]) : null,
                "Receptionist" => row["ReceptionistID"] != DBNull.Value ? Convert.ToInt32(row["ReceptionistID"]) : null,
                "Dentist" => row["DentistID"] != DBNull.Value ? Convert.ToInt32(row["DentistID"]) : null,
                "Patient" => row["PatientID"] != DBNull.Value ? Convert.ToInt32(row["PatientID"]) : null,
                _ => null
            };
        }
    }
}