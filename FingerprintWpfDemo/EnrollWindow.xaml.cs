using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FingerprintWpfDemo
{
    public partial class EnrollWindow : Window
    {
        private readonly FingerprintService _service;
        private readonly BiometricApiClient _api;

        // Adjust these for your real deployment
        private const string SITE_ID = "SITE-001";
        private static readonly string DEVICE_ID = Environment.MachineName;

        public EnrollWindow(FingerprintService service, BiometricApiClient api)
        {
            InitializeComponent();
            _service = service;
            _api = api;
        }

        private void AppendLog(string text)
        {
            txtLog.AppendText(text + "\r\n");
            txtLog.ScrollToEnd();
        }

        /// <summary>
        /// Called from ManageWindow to start re-enrol for a known name.
        /// </summary>
        public void StartReEnrolFor(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            txtName.Text = name;
            btnReEnrol_Click(this, new RoutedEventArgs());
        }

        // ------------- ENROL (NEW USER) -------------

        private async void btnEnrol_Click(object sender, RoutedEventArgs e)
        {
            string name = txtName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                AppendLog("Enter a name to enrol.");
                return;
            }

            btnEnrol.IsEnabled = false;
            btnReEnrol.IsEnabled = false;
            txtLog.Clear();

            AppendLog($"Checking if '{name}' is available on server...");

            var search = await _api.SearchEmployeesAsync(name);
            if (!search.Success)
            {
                AppendLog("Server name check failed: " + search.Error);
                txtResult.Text = "Cannot enrol: server name check failed.";
                btnEnrol.IsEnabled = true;
                btnReEnrol.IsEnabled = true;
                return;
            }

            // server uses LIKE %q%, so filter to exact matches
            bool exactExists = search.Results.Any(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

            if (exactExists)
            {
                AppendLog($"A user called '{name}' is already enrolled on the server.");
                AppendLog("Use Re-enrol or choose a different name.");
                txtResult.Text = "Name already in use.";
                btnEnrol.IsEnabled = true;
                btnReEnrol.IsEnabled = true;
                return;
            }

            // --- name is free; continue with local enrol flow ---
            AppendLog($"Name '{name}' is available. Starting local enrolment for '{name}'...");

            bool success = false;
            await Task.Run(() =>
            {
                success = _service.Enrol(name, msg =>
                {
                    Dispatcher.Invoke(() => AppendLog(msg));
                });
            });

            if (!success)
            {
                AppendLog($"Local enrolment FAILED for '{name}'.");
                txtResult.Text = "Local enrolment failed.";
                btnEnrol.IsEnabled = true;
                btnReEnrol.IsEnabled = true;
                return;
            }

            AppendLog($"Local enrolment COMPLETE for '{name}'.");
            AppendLog("Serialising template and sending to server...");

            await SendTemplateToServer(name);

            txtResult.Text = "Enrolment complete.";
            btnEnrol.IsEnabled = true;
            btnReEnrol.IsEnabled = true;
        }

        // ------------- RE-ENROL (EXISTING USER) -------------

        private async void btnReEnrol_Click(object sender, RoutedEventArgs e)
        {
            string name = txtName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                AppendLog("Enter a name to re-enrol.");
                return;
            }

            btnEnrol.IsEnabled = false;
            btnReEnrol.IsEnabled = false;
            txtLog.Clear();

            AppendLog($"Searching for existing enrolments matching '{name}'...");

            var search = await _api.SearchEmployeesAsync(name);
            if (!search.Success)
            {
                AppendLog("Server search failed: " + search.Error);
                txtResult.Text = "Cannot re-enrol: server search failed.";
                btnEnrol.IsEnabled = true;
                btnReEnrol.IsEnabled = true;
                return;
            }

            var matches = search.Results;
            if (matches.Count == 0)
            {
                AppendLog($"No existing enrolments found for '{name}'.");
                txtResult.Text = "No matching users on server.";
                btnEnrol.IsEnabled = true;
                btnReEnrol.IsEnabled = true;
                return;
            }

            EmployeeSearchResult selected;

            if (matches.Count == 1)
            {
                selected = matches[0];
                AppendLog($"Found one match: {selected.Name} ({selected.Ref}, Id={selected.Id}).");
            }
            else
            {
                // Try exact name match first
                var exactMatches = matches
                    .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (exactMatches.Count == 1)
                {
                    selected = exactMatches[0];
                    AppendLog($"Using exact match: {selected.Name} ({selected.Ref}, Id={selected.Id}).");
                }
                else
                {
                    // TODO: replace this with a proper selection dialog
                    selected = matches[0];
                    AppendLog($"Multiple matches found, using first: {selected.Name} ({selected.Ref}, Id={selected.Id}).");
                }
            }

            // From here on you have a specific server record selected
            AppendLog($"Starting local re-enrol for '{selected.Name}' (Id={selected.Id})...");

            bool success = false;
            await Task.Run(() =>
            {
                success = _service.Enrol(selected.Name, msg =>
                {
                    Dispatcher.Invoke(() => AppendLog(msg));
                });
            });

            if (!success)
            {
                AppendLog($"Local re-enrolment FAILED for '{selected.Name}'.");
                txtResult.Text = "Local re-enrolment failed.";
                btnEnrol.IsEnabled = true;
                btnReEnrol.IsEnabled = true;
                return;
            }

            AppendLog($"Local re-enrolment COMPLETE for '{selected.Name}'.");
            AppendLog("Serialising template and sending re-enrol to server...");

            await SendReEnrolTemplateToServer(selected);

            txtResult.Text = "Re-enrolment complete.";
            btnEnrol.IsEnabled = true;
            btnReEnrol.IsEnabled = true;
        }

        // ------------- SEND TEMPLATE TO SERVER -------------

        /// <summary>
        /// First-time enrol: send to /api/enrol.
        /// </summary>
        private async Task SendTemplateToServer(string name)
        {
            var bytes = _service.GetTemplateBytes(name, msg => AppendLog(msg));
            if (bytes == null)
            {
                AppendLog("Cannot send to server: failed to get template bytes.");
                return;
            }

            string templateBase64 = Convert.ToBase64String(bytes);

            var result = await _api.EnrolAsync(
                SITE_ID,
                DEVICE_ID,
                name,
                templateBase64);

            if (result.Success && result.Response != null)
            {
                var resp = result.Response;
                AppendLog(string.Format(
                    "Server enrol OK. EnrollmentId: {0}, EmployeeRef: {1}, Status: {2}",
                    resp.enrollmentIdFormatted, resp.employeeRef, resp.status));
                _service.SetEnrollmentId(name, resp.enrollmentId);
            }
            else
            {
                AppendLog("Server enrol FAILED: " + result.Error);
            }
        }

        /// <summary>
        /// Re-enrol: send to /api/reenrol for a specific enrollmentId.
        /// </summary>
        private async Task SendReEnrolTemplateToServer(EmployeeSearchResult selected)
        {
            var bytes = _service.GetTemplateBytes(selected.Name, msg => AppendLog(msg));
            if (bytes == null)
            {
                AppendLog("Cannot send to server: failed to get template bytes.");
                return;
            }

            string templateBase64 = Convert.ToBase64String(bytes);

            var result = await _api.ReEnrolAsync(
                selected.Id,
                templateBase64);

            if (result.Success && result.Response != null)
            {
                var resp = result.Response;
                AppendLog(string.Format(
                    "Server re-enrol OK. EnrollmentId: {0}, EmployeeRef: {1}, Status: {2}",
                    resp.enrollmentIdFormatted, resp.employeeRef, resp.status));

                _service.SetEnrollmentId(selected.Name, resp.enrollmentId);
            }
            else
            {
                AppendLog("Server re-enrol FAILED: " + result.Error);
            }
        }
    }
}
