using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FingerprintWpfDemo
{
    public partial class MainWindow : Window
    {
        private readonly FingerprintService _service = new FingerprintService();
        private readonly BiometricApiClient _apiClient = new BiometricApiClient();

        // Change this PIN to whatever you want
        private const string ADMIN_PIN = "1234";

        public MainWindow()
        {
            InitializeComponent();

            // Reader init
            if (!_service.Initialize())
            {
                txtStatus.Text = "Reader: offline";
                DeviceStatusBrush.Color = Colors.OrangeRed;
            }
            else
            {
                txtStatus.Text = "Reader: ready";
                DeviceStatusBrush.Color = Colors.LimeGreen;
            }

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckApiHealthAsync();
        }

        private async Task CheckApiHealthAsync()
        {
            txtApiStatus.Text = "API: checking...";
            ApiStatusBrush.Color = Colors.Goldenrod;

            string statusText = await _apiClient.GetHealthStatusTextAsync();
            txtApiStatus.Text = statusText;

            if (statusText.StartsWith("API: OK", StringComparison.OrdinalIgnoreCase))
            {
                ApiStatusBrush.Color = Colors.LimeGreen;
            }
            else
            {
                ApiStatusBrush.Color = Colors.OrangeRed;
            }
        }

        private void btnIdentify_Click(object sender, RoutedEventArgs e)
        {
            var win = new IdentifyWindow(_service);
            win.Owner = this;
            win.Show();
        }

        private void btnManagement_Click(object sender, RoutedEventArgs e)
        {
            var pinWindow = new PinWindow(ADMIN_PIN)
            {
                Owner = this
            };

            bool? result = pinWindow.ShowDialog();

            if (result == true && pinWindow.IsAuthorized)
            {
                var mgmtWin = new ManagementWindow(_service);
                mgmtWin.Owner = this;
                mgmtWin.Show();
            }
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

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _service.Dispose();
            _apiClient.Dispose();
            Application.Current.Shutdown();
        }
    }
}
