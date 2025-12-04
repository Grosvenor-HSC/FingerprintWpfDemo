using System.Windows;

namespace FingerprintWpfDemo
{
    public partial class ManagementWindow : Window
    {
        private readonly FingerprintService _service;
        private readonly BiometricApiClient _api;

        public ManagementWindow(FingerprintService service, BiometricApiClient api)
        {
            InitializeComponent();
            _service = service;
            _api = api;
        }

        private void btnEnroll_Click(object sender, RoutedEventArgs e)
        {
            var win = new EnrollWindow(_service, _api);
            win.Owner = this;
            win.Show();
        }

        private void btnUsers_Click(object sender, RoutedEventArgs e)
        {
            var win = new ManageWindow(_service, _api);
            win.Owner = this;
            win.Show();
        }
    }
}
