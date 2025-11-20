using System.Linq;
using System.Windows;

namespace FingerprintWpfDemo
{
    public partial class ManageWindow : Window
    {
        private readonly FingerprintService _service;

        public ManageWindow(FingerprintService service)
        {
            InitializeComponent();
            _service = service;
            RefreshList();
        }

        private void RefreshList()
        {
            lstUsers.Items.Clear();
            foreach (var name in _service.Templates.Keys.OrderBy(x => x))
            {
                lstUsers.Items.Add(name);
            }
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshList();
        }

        private void btnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (lstUsers.SelectedItem == null)
            {
                MessageBox.Show("Select a user to remove.");
                return;
            }

            string name = lstUsers.SelectedItem.ToString();
            var answer = MessageBox.Show(
                $"Remove template for '{name}'?",
                "Confirm remove",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer == MessageBoxResult.Yes)
            {
                if (_service.RemoveTemplate(name))
                {
                    RefreshList();
                }
                else
                {
                    MessageBox.Show("Failed to remove (not found).");
                }
            }
        }
    }
}
