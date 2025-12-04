using System;
using System.Linq;
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
            txtResult.Text = "Looking up user...";
            txtLog.Clear();

            // 1) Search on the server for matching employees
            var search = await App.ApiClient.SearchEmployeesAsync(name);
            if (!search.Success)
            {
                AppendLog("Server search failed: " + search.Error);
                txtResult.Text = "Server search failed.";
                btnScan.IsEnabled = true;
                return;
            }

            var matches = search.Results;
            if (matches.Count == 0)
            {
                AppendLog($"No enrolled users found matching '{name}'.");
                txtResult.Text = "No matching users on server.";
                btnScan.IsEnabled = true;
                return;
            }

            // For now, if multiple matches: pick exact match, otherwise first match
            var exactMatches = matches
                .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var selected = exactMatches.Count == 1 ? exactMatches[0] : matches[0];

            AppendLog($"Selected: {selected.Name} (Id={selected.Id})");
            txtResult.Text = $"Selected {selected.Name} – place finger...";

            // 2) Ensure we have a local template for this user
            if (!_service.HasTemplate(selected.Name))
            {
                AppendLog("No local template found. Fetching from server...");
                txtResult.Text = "Downloading template...";

                var tmpl = await App.ApiClient.GetTemplateAsync(selected.Id);
                if (!tmpl.Success)
                {
                    AppendLog("Template download failed: " + tmpl.Error);
                    txtResult.Text = "Cannot download template.";
                    btnScan.IsEnabled = true;
                    return;
                }

                try
                {
                    // Server now returns base64, not hex
                    byte[] templateBytes = Convert.FromBase64String(tmpl.TemplateBase64);
                    _service.SaveTemplate(selected.Name, templateBytes);
                    AppendLog("Template downloaded and cached locally.");
                }
                catch (Exception ex)
                {
                    AppendLog("Error decoding or saving template: " + ex.Message);
                    txtResult.Text = "Template decode error.";
                    btnScan.IsEnabled = true;
                    return;
                }
            }
            else
            {
                AppendLog("Using local cached template.");
            }

            // 3) Local verification against template
            txtResult.Text = "Waiting for finger...";
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
                txtResult.Text = $"No match for '{selected.Name}'.";
                btnScan.IsEnabled = true;
                return;
            }

            AppendLog($"Local verification successful. Score={result.score}, Confidence={result.confidence:0.00}");
            txtResult.Text = $"Match for '{selected.Name}' ({result.confidence * 100:0}% confidence)";

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

            var scanResult = await App.ApiClient.ScanAsync(
                enrollmentId.Value,
                result.confidence,
                selected.Name
            );

            if (!scanResult.Success)
            {
                AppendLog("Server scan FAILED: " + scanResult.Error);
                txtResult.Text = "Scan failed (server).";
                btnScan.IsEnabled = true;
                return;
            }

            string action = scanResult.Response.action; // "IN" or "OUT"
            AppendLog($"Server registered action: {action}");
            txtResult.Text = $"Clock {action}";

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
