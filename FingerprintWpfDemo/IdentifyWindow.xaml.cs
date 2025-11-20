using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace FingerprintWpfDemo
{
    public partial class IdentifyWindow : Window
    {
        private readonly FingerprintService _service;

        public IdentifyWindow(FingerprintService service)
        {
            InitializeComponent();
            _service = service;
        }

        private void AppendLog(string text)
        {
            txtLog.AppendText(text + "\r\n");
            txtLog.ScrollToEnd();
        }

        private async void btnScan_Click(object sender, RoutedEventArgs e)
        {
            string name = txtName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                txtResult.Text = "Enter a name first.";
                return;
            }

            btnScan.IsEnabled = false;
            txtResult.Text = "Waiting for finger...";
            txtLog.Clear();

            (bool success, int score, double confidence) result = (false, int.MaxValue, 0);

            await Task.Run(() =>
            {
                result = _service.Verify(name, msg =>
                {
                    Dispatcher.Invoke(() => AppendLog(msg));
                });
            });

            if (result.success)
            {
                txtResult.Text = $"Match for '{name}' ({result.confidence * 100:0}% confidence)";
            }
            else
            {
                txtResult.Text = $"No match for '{name}'.";
            }

            btnScan.IsEnabled = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CardBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }
    }
}
