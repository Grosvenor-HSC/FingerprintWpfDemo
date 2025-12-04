using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FingerprintWpfDemo
{
    public partial class ManageWindow : Window
    {
        private readonly FingerprintService _service;
        private readonly BiometricApiClient _api;

        private readonly ObservableCollection<EmployeeSearchResult> _users =
            new ObservableCollection<EmployeeSearchResult>();

        public ManageWindow(FingerprintService service, BiometricApiClient api)
        {
            InitializeComponent();
            _service = service;
            _api = api;

            dgUsers.ItemsSource = _users;

            Loaded += ManageWindow_Loaded;
        }

        private async void ManageWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshListAsync();
        }

        private async void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshListAsync();
        }

        private async Task RefreshListAsync()
        {
            string filter = txtFilter.Text?.Trim() ?? string.Empty;

            btnRefresh.IsEnabled = false;
            try
            {
                var result = await _api.SearchEmployeesAsync(filter);

                if (!result.Success)
                {
                    MessageBox.Show(
                        $"Failed to load users from server:\n{result.Error}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                _users.Clear();
                foreach (var u in result.Results.OrderBy(u => u.Name).ThenBy(u => u.Id))
                {
                    _users.Add(u);
                }

                if (_users.Count == 0)
                {
                    // Optional info; DataGrid will just be blank
                    // MessageBox.Show("No enrolled users found.", "Info",
                    //     MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unexpected error while loading users:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                btnRefresh.IsEnabled = true;
            }
        }

        private void btnRemoveLocal_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgUsers.SelectedItem as EmployeeSearchResult;
            if (selected == null)
            {
                MessageBox.Show("Select a user first.");
                return;
            }

            string name = selected.Name;

            var answer = MessageBox.Show(
                $"This only removes the *local* template for '{name}' on this machine.\n" +
                $"The server enrollment remains.\n\nDo you want to continue?",
                "Remove Local Template",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) return;

            // This will only succeed if this app instance has an in-memory template for that name
            if (_service != null && _service.GetType().GetMethod("RemoveTemplate") != null)
            {
                try
                {
                    // You already had _service.RemoveTemplate(name) in the original code,
                    // so this should exist. If it doesn't, you can remove this entire button.
                    var removed = (bool)_service.GetType()
                        .GetMethod("RemoveTemplate")
                        .Invoke(_service, new object[] { name });

                    if (!removed)
                    {
                        MessageBox.Show("Local template not found.", "Info",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Local template removed.", "Info",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Error removing local template:\n{ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show(
                    "Local template removal is not implemented on this build.",
                    "Info",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private EmployeeSearchResult GetUserFromMenuSender(object sender)
        {
            var menuItem = sender as MenuItem;
            if (menuItem == null)
                return dgUsers.SelectedItem as EmployeeSearchResult;

            var ctxMenu = menuItem.Parent as ContextMenu;
            var row = ctxMenu?.PlacementTarget as DataGridRow;
            return row?.Item as EmployeeSearchResult ?? dgUsers.SelectedItem as EmployeeSearchResult;
        }

        private void MenuReEnrol_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgUsers.SelectedItem as EmployeeSearchResult;
            if (selected == null)
            {
                MessageBox.Show("Select a user first.");
                return;
            }

            // Open the enrol window and kick off re-enrol for this user
            var win = new EnrollWindow(_service, _api);
            win.Owner = this;
            win.Show();

            win.StartReEnrolFor(selected.Name);
        }

        private async void MenuDeleteUser_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgUsers.SelectedItem as EmployeeSearchResult;
            if (selected == null)
            {
                MessageBox.Show("Select a user first.");
                return;
            }

            var answer = MessageBox.Show(
                $"This will delete '{selected.Name}' from the server and remove any local template " +
                $"on this machine.\n\nAre you sure?",
                "Delete user",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;

            // Delete on server
            var result = await _api.DeleteEnrollmentAsync(selected.Id);
            if (!result.Success)
            {
                MessageBox.Show(
                    $"Failed to delete on server:\n{result.Error}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // Remove local template (if present) – ignore errors
            try
            {
                var removeMethod = _service?.GetType().GetMethod("RemoveTemplate");
                if (removeMethod != null)
                {
                    removeMethod.Invoke(_service, new object[] { selected.Name });
                }
            }
            catch { }

            // Remove from list/grid
            _users.Remove(selected);
        }

    }
}
