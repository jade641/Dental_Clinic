using Microsoft.Extensions.Logging;
using Dental_Clinic.Services;

namespace Dental_Clinic
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            // Register Database Services as Singletons
            builder.Services.AddSingleton<DatabaseService>();
            builder.Services.AddSingleton<LocalDatabaseService>();
                
            // Register Sync Service
            builder.Services.AddSingleton<SyncService>();
                
            // Register Auth Service
            builder.Services.AddScoped<AuthService>();
                
            // Register Session Service
            builder.Services.AddSingleton<SessionService>();

#if DEBUG
        		builder.Services.AddBlazorWebViewDeveloperTools();
        		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
