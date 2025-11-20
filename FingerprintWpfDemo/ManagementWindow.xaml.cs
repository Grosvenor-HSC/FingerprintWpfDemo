using System.Windows;

namespace FingerprintWpfDemo
{
    public partial class ManagementWindow : Window
    {
        private readonly FingerprintService _service;

        public ManagementWindow(FingerprintService service)
        {
            InitializeComponent();
            _service = service;
        }

        private void btnEnroll_Click(object sender, RoutedEventArgs e)
        {
            var win = new EnrollWindow(_service);
            win.Owner = this;
            win.Show();
        }

        private void btnUsers_Click(object sender, RoutedEventArgs e)
        {
            var win = new ManageWindow(_service);
            win.Owner = this;
            win.Show();
        }
    }
}
