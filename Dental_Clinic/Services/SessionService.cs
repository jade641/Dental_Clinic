using Dental_Clinic.Models;
using System.Text.Json;

namespace Dental_Clinic.Services
{
 public class SessionService
    {
        private const string SessionKey = "UserSession";
        private UserSession? _currentSession;

        public async Task<UserSession?> GetCurrentSessionAsync()
        {
 if (_currentSession != null)
              return _currentSession;

            try
            {
   var sessionJson = await SecureStorage.GetAsync(SessionKey);
         if (!string.IsNullOrEmpty(sessionJson))
                {
                    _currentSession = JsonSerializer.Deserialize<UserSession>(sessionJson);
        }
         }
 catch
 {
  _currentSession = null;
            }

   return _currentSession;
     }

   public async Task SaveSessionAsync(UserSession session)
        {
            _currentSession = session;
      var json = JsonSerializer.Serialize(session);
            await SecureStorage.SetAsync(SessionKey, json);
        }

        public async Task ClearSessionAsync()
        {
     _currentSession = null;
            SecureStorage.Remove(SessionKey);
            await Task.CompletedTask;
        }

        public bool IsAuthenticated => _currentSession != null;

        public string? CurrentUserRole => _currentSession?.Role;
  
        public string? CurrentUserName => _currentSession?.UserName;
  }
}
