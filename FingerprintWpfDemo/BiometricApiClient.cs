using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace FingerprintWpfDemo
{
    public class BiometricApiClient : IDisposable
    {
        // Health endpoint to check
        private const string HEALTH_URL = "https://biometric.grosvenorhsc.org/health";
        // For local dev instead:
        // private const string HEALTH_URL = "http://127.0.0.1:7072/health";

        // Cloudflare Access credentials (dev only – treat as secrets in real life)
        private const string CF_ACCESS_CLIENT_ID = "07e1266722cc377abd861ae3ebd91ea8.access";
        private const string CF_ACCESS_CLIENT_SECRET = "baddeac1d9e0179750eaff34fdf34a10f3c8b25b7e35147b21041fab9c5e9e0b";

        private readonly HttpClient _httpClient;
        private bool _headersInitialised;

        public BiometricApiClient()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        private void EnsureHeaders()
        {
            if (_headersInitialised) return;

            _httpClient.DefaultRequestHeaders.Add("CF-Access-Client-Id", CF_ACCESS_CLIENT_ID);
            _httpClient.DefaultRequestHeaders.Add("CF-Access-Client-Secret", CF_ACCESS_CLIENT_SECRET);

            _headersInitialised = true;
        }

        /// <summary>
        /// Returns a user-friendly status string, e.g. "API: OK" or "API: error 403 Forbidden".
        /// </summary>
        public async Task<string> GetHealthStatusTextAsync()
        {
            try
            {
                EnsureHeaders();

                HttpResponseMessage response = await _httpClient.GetAsync(HEALTH_URL);
                if (response.IsSuccessStatusCode)
                {
                    return "API: OK";
                }

                return $"API: error {(int)response.StatusCode} {response.ReasonPhrase}";
            }
            catch (Exception)
            {
                return "API: unreachable";
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
