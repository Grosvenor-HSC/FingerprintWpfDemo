using System.Threading.Tasks;
using System.Windows;

namespace FingerprintWpfDemo
{
    public partial class EnrollWindow : Window
    {
        private readonly FingerprintService _service;

        public EnrollWindow(FingerprintService service)
        {
            InitializeComponent();
            _service = service;
        }

        private void AppendLog(string text)
        {
            txtLog.AppendText(text + "\r\n");
            txtLog.ScrollToEnd();
        }

        private async void btnEnrol_Click(object sender, RoutedEventArgs e)
        {
            string name = txtName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                AppendLog("Enter a name to enrol.");
                return;
            }

            if (_service.HasTemplate(name))
            {
                AppendLog($"'{name}' already has a template. Use Re-enrol.");
                return;
            }

            AppendLog($"Starting enrolment for '{name}'...");

            btnEnrol.IsEnabled = false;
            btnReEnrol.IsEnabled = false;

            bool ok = false;

            // Run enrolment on a background thread, but push log messages to UI
            await Task.Run(() =>
            {
                ok = _service.Enrol(name, msg =>
                {
                    Dispatcher.Invoke(() => AppendLog(msg));
                });
            });

            if (ok)
                AppendLog($"Enrolment COMPLETE for '{name}'.");
            else
                AppendLog($"Enrolment FAILED for '{name}'.");

            btnEnrol.IsEnabled = true;
            btnReEnrol.IsEnabled = true;
        }

        private async void btnReEnrol_Click(object sender, RoutedEventArgs e)
        {
            string name = txtName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                AppendLog("Enter a name to re-enrol.");
                return;
            }

            if (!_service.HasTemplate(name))
            {
                AppendLog($"No template found for '{name}'. Enrol as new.");
                return;
            }

            var result = MessageBox.Show(
                $"Replace existing template for '{name}'?",
                "Confirm re-enrol",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                AppendLog("Re-enrol cancelled.");
                return;
            }

            AppendLog($"Re-enrolling '{name}'...");

            btnEnrol.IsEnabled = false;
            btnReEnrol.IsEnabled = false;

            bool ok = false;

            await Task.Run(() =>
            {
                ok = _service.Enrol(name, msg =>
                {
                    Dispatcher.Invoke(() => AppendLog(msg));
                });
            });

            if (ok)
                AppendLog($"Re-enrolment COMPLETE for '{name}'.");
            else
                AppendLog($"Re-enrolment FAILED for '{name}'.");

            btnEnrol.IsEnabled = true;
            btnReEnrol.IsEnabled = true;
        }
    }
}
