using System.Windows;

namespace FingerprintWpfDemo
{
    public partial class PinWindow : Window
    {
        private readonly string _expectedPin;

        public bool IsAuthorized { get; private set; }

        public PinWindow(string expectedPin)
        {
            InitializeComponent();
            _expectedPin = expectedPin;
            IsAuthorized = false;
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            string entered = pwdPin.Password.Trim();

            if (entered == _expectedPin)
            {
                IsAuthorized = true;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Incorrect PIN.", "Access denied",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                IsAuthorized = false;
                // keep window open so they can try again or cancel
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsAuthorized = false;
            DialogResult = false;
            Close();
        }
    }
}
