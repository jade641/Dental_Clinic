using Microsoft.Extensions.Logging;
using Dental_Clinic.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Dental_Clinic
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            // Load configuration files so appsettings.json and development settings are available
            builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);

            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddRadzenComponents();

            // Register Database Services as Singletons
            builder.Services.AddSingleton<DatabaseService>();
            builder.Services.AddSingleton<LocalDatabaseService>();

            // Register Sync Service
            builder.Services.AddSingleton<SyncService>();
                
            // Register AppointmentService
            builder.Services.AddScoped<AppointmentService>();
                
            // Register Auth Service
            builder.Services.AddScoped<AuthService>();

            // Register Session Service
            builder.Services.AddSingleton<SessionService>();

            // Register Email Service
            builder.Services.AddSingleton<EmailService>();

            // Register HttpClient
            builder.Services.AddSingleton<HttpClient>();

            // Register GoogleAuthService using DI factory so IConfiguration is injected correctly
            builder.Services.AddSingleton<GoogleAuthService>(sp =>
            {
                var db = sp.GetRequiredService<DatabaseService>();
                var http = sp.GetRequiredService<HttpClient>();
                var cfg = sp.GetRequiredService<IConfiguration>();
                return new GoogleAuthService(db, http, cfg);
            });

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}