namespace Dental_Clinic
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            RegisterGlobalExceptionHandlers();
        }

        private void RegisterGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[GLOBAL] UnhandledException: {e.ExceptionObject}");
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[GLOBAL] UnobservedTaskException: {e.Exception}" );
                e.SetObserved();
            };
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new MainPage()) { Title = "Dental Clinic" };
        }

        // Handle protocol activation (OAuth callback)
        protected override void OnAppLinkRequestReceived(Uri uri)
        {
            base.OnAppLinkRequestReceived(uri);

            // Handle Google OAuth callback
            if (uri.Scheme.StartsWith("com.googleusercontent.apps"))
            {
                // Fallback: we are not using Shell, so just update navigation in Blazor via query
                var navigationUri = $"/google-callback{uri.Query}";
                Microsoft.Maui.Controls.Application.Current?.Dispatcher.Dispatch(() =>
                {
                    // If a Shell exists use it, else rely on Blazor routing
                    if (Shell.Current is not null)
                    {
                        Shell.Current.GoToAsync(navigationUri);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[AppLink] Received OAuth callback: {uri}");
                    }
                });
            }
        }
    }
}
