using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace Dental_Clinic
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            // Try compiled XAML first
            InitializeComponent();
        }
    }
}
