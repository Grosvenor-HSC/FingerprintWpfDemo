using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FingerprintWpfDemo
{
    public partial class IdentifyWindow : Window
    {
        private readonly FingerprintService _service;

        private enum ScanState
        {
            Neutral,
            Success,
            Retry,
            Error
        }

        public IdentifyWindow(FingerprintService service)
        {
            InitializeComponent();
            _service = service;

            SetScanState(ScanState.Neutral, "Ready.");
        }

        private void AppendLog(string text)
        {
            txtLog.AppendText(text + "\r\n");
            txtLog.ScrollToEnd();
        }

        private void SetScanState(ScanState state, string message)
        {
            txtResult.Text = message;

            Brush bg;
            Brush fg;

            switch (state)
            {
                case ScanState.Success:      // GREEN
                    bg = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // #2E7D32
                    fg = Brushes.White;
                    break;

                case ScanState.Retry:        // AMBER
                    bg = new SolidColorBrush(Color.FromRgb(0xFB, 0x8C, 0x00)); // #FB8C00
                    fg = Brushes.Black;
                    break;

                case ScanState.Error:        // RED
                    bg = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)); // #C62828
                    fg = Brushes.White;
                    break;

                default:                     // NEUTRAL
                    bg = new SolidColorBrush(Color.FromRgb(0x02, 0x06, 0x17)); // matches your dark card
                    fg = (Brush)FindResource("TextSecondaryBrush");
                    break;
            }

            if (ResultPanel != null)
            {
                ResultPanel.Background = bg;
            }

            txtResult.Foreground = fg;
        }

        private async void btnScan_Click(object sender, RoutedEventArgs e)
        {
            string name = txtName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                SetScanState(ScanState.Error, "Enter a name first.");
                return;
            }

            btnScan.IsEnabled = false;
            txtLog.Clear();
            SetScanState(ScanState.Neutral, "Looking up user...");

            // 1) Search on the server for matching employees
            var search = await App.ApiClient.SearchEmployeesAsync(name);
            if (!search.Success)
            {
                AppendLog("Server search failed: " + search.Error);
                SetScanState(ScanState.Error, "Server search failed.");
                btnScan.IsEnabled = true;
                return;
            }

            var matches = search.Results;
            if (matches.Count == 0)
            {
                AppendLog($"No enrolled users found matching '{name}'.");
                SetScanState(ScanState.Error, "No matching users on server.");
                btnScan.IsEnabled = true;
                return;
            }

            // For now, if multiple matches: pick exact match, otherwise first match
            var exactMatches = matches
                .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var selected = exactMatches.Count == 1 ? exactMatches[0] : matches[0];

            AppendLog($"Selected: {selected.Name} (Id={selected.Id})");
            SetScanState(ScanState.Neutral, $"Selected {selected.Name} – preparing fingerprint...");

            // 2) Ensure we have a local template for this user
            if (!_service.HasTemplate(selected.Name))
            {
                AppendLog("No local template found. Fetching from server...");
                SetScanState(ScanState.Neutral, "Downloading fingerprint template...");

                var tmpl = await App.ApiClient.GetTemplateAsync(selected.Id);
                if (!tmpl.Success)
                {
                    AppendLog("Template download failed: " + tmpl.Error);
                    SetScanState(ScanState.Error, "Cannot download template.");
                    btnScan.IsEnabled = true;
                    return;
                }

                try
                {
                    byte[] templateBytes = Convert.FromBase64String(tmpl.TemplateBase64);
                    _service.SaveTemplate(selected.Name, templateBytes);
                    AppendLog("Template downloaded and cached locally.");
                }
                catch (Exception ex)
                {
                    AppendLog("Error decoding or saving template: " + ex.Message);
                    SetScanState(ScanState.Error, "Template decode error.");
                    btnScan.IsEnabled = true;
                    return;
                }
            }
            else
            {
                AppendLog("Using local cached template.");
            }

            // 3) Local verification against template
            SetScanState(ScanState.Neutral, "Place finger on the scanner...");
            AppendLog($"Scan finger for '{selected.Name}' to verify...");

            (bool success, int score, double confidence) result = (false, int.MaxValue, 0);

            await Task.Run(() =>
            {
                result = _service.Verify(selected.Name, msg =>
                {
                    Dispatcher.Invoke(() => AppendLog(msg));
                });
            });

            if (!result.success)
            {
                // Template exists but the match failed – most likely bad placement / wrong finger
                AppendLog($"Local verification failed for '{selected.Name}'.");
                SetScanState(ScanState.Retry, "Fingerprint not recognised. Try again.");
                btnScan.IsEnabled = true;
                return;
            }

            AppendLog($"Local verification successful. Score={result.score}, Confidence={result.confidence:0.00}");
            SetScanState(ScanState.Neutral, $"Match for '{selected.Name}' ({result.confidence * 100:0}% confidence)");

            // 4) Get / cache enrollmentId locally if not already stored
            int? enrollmentId = _service.GetEnrollmentId(selected.Name);
            if (enrollmentId == null)
            {
                AppendLog("No local enrollmentId cached. Using server Id and caching it.");
                _service.SetEnrollmentId(selected.Name, selected.Id);
                enrollmentId = selected.Id;
            }

            AppendLog($"Using enrollmentId={enrollmentId.Value} for server event logging.");

            // 5) Send scan event to server (IN/OUT)
            AppendLog("Sending scan event to server...");
            SetScanState(ScanState.Neutral, "Logging clock event...");

            var scanResult = await App.ApiClient.ScanAsync(
                enrollmentId.Value,
                result.confidence,
                selected.Name
            );

            if (!scanResult.Success || scanResult.Response == null)
            {
                AppendLog("Server scan FAILED: " + scanResult.Error);
                SetScanState(ScanState.Error, "Scan failed on server. Try again.");
                btnScan.IsEnabled = true;
                return;
            }

            string action = scanResult.Response.action; // "IN" or "OUT"
            AppendLog($"Server registered action: {action}");
            SetScanState(ScanState.Success, $"Clock {action} OK");

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
