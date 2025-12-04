using System;
using System.Windows;

namespace FingerprintWpfDemo
{
    public partial class App : Application
    {
        public static BiometricApiClient ApiClient { get; private set; }
        public static FingerprintService FingerprintService { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Create shared API client once
            ApiClient = new BiometricApiClient();

            // Create shared fingerprint service once
            FingerprintService = new FingerprintService();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ApiClient?.Dispose();
            base.OnExit(e);
        }
    }
}
