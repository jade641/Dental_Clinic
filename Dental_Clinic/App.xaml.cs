namespace Dental_Clinic
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
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
                // Navigate to the callback page with the full URI as a parameter
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var navigationUri = $"/google-callback{uri.Query}";
                    Shell.Current?.GoToAsync(navigationUri);
                });
            }
        }
    }
}
