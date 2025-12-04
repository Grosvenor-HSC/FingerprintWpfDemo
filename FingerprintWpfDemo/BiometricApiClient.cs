using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FingerprintWpfDemo
{
    public class EmployeeSearchResult
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("ref")]
        public string Ref { get; set; }
    }

    public class BiometricApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _cfHeadersInitialised;

        // Base API URL
        private const string BASE_URL = "https://biometric.grosvenorhsc.org";

        // Cloudflare Access credentials
        private const string CF_ACCESS_CLIENT_ID = "07e1266722cc377abd861ae3ebd91ea8.access";
        private const string CF_ACCESS_CLIENT_SECRET = "baddeac1d9e0179750eaff34fdf34a10f3c8b25b7e35147b21041fab9c5e9e0b";

        // API auth
        private const string API_TOKEN = "aB1cD2eF3gH4iJ5kL6mN7oP8qR9sT0uVwXyZabCdefG=";
        private const string HMAC_SECRET_BASE64 = "Y/gB1XfDubCDGpZprcl9HPR7KKKcuWqd89QBF/vCSEEy3u89orIS/e0shpA2+CwsoqpFvyUySEaLS2XHS/z0+g==";

        public BiometricApiClient()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        private void EnsureCloudflareHeaders()
        {
            if (_cfHeadersInitialised) return;

            _httpClient.DefaultRequestHeaders.Add("CF-Access-Client-Id", CF_ACCESS_CLIENT_ID);
            _httpClient.DefaultRequestHeaders.Add("CF-Access-Client-Secret", CF_ACCESS_CLIENT_SECRET);

            _cfHeadersInitialised = true;
        }

        // ---------- HEALTH CHECK ----------

        public async Task<string> GetHealthStatusTextAsync()
        {
            EnsureCloudflareHeaders();

            try
            {
                var resp = await _httpClient.GetAsync(BASE_URL + "/health");
                if (resp.IsSuccessStatusCode)
                {
                    return "API: OK";
                }

                return $"API: error {(int)resp.StatusCode} {resp.ReasonPhrase}";
            }
            catch
            {
                return "API: unreachable";
            }
        }

        // ---------- HMAC HELPERS ----------

        private static string BytesToHexLower(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                sb.Append(b.ToString("x2")); // lowercase hex
            }
            return sb.ToString();
        }

        private void AddSignedHeaders(HttpRequestMessage req, byte[] bodyBytes, string method, string path)
        {
            // 1) Timestamp
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // 2) Body hash (hex sha256)
            string bodyHash;
            using (var sha = SHA256.Create())
            {
                var hashBytes = sha.ComputeHash(bodyBytes);
                bodyHash = BytesToHexLower(hashBytes);
            }

            // 3) Message
            string message = string.Format("{0}\n{1}\n{2}\n{3}",
                timestamp,
                method.ToUpperInvariant(),
                path,
                bodyHash);

            // 4) HMAC
            byte[] keyBytes = Convert.FromBase64String(HMAC_SECRET_BASE64);
            byte[] msgBytes = Encoding.UTF8.GetBytes(message);
            byte[] hmacBytes;
            using (var hmac = new HMACSHA256(keyBytes))
            {
                hmacBytes = hmac.ComputeHash(msgBytes);
            }

            string signature = Convert.ToBase64String(hmacBytes);

            // 5) Headers
            req.Headers.Remove("X-Api-Token");
            req.Headers.Remove("X-HMAC-Timestamp");
            req.Headers.Remove("X-HMAC-Signature");

            req.Headers.Add("X-Api-Token", API_TOKEN);
            req.Headers.Add("X-HMAC-Timestamp", timestamp);
            req.Headers.Add("X-HMAC-Signature", signature);
        }

        // ---------- EMPLOYEE SEARCH ----------

        public async Task<(bool Success, List<EmployeeSearchResult> Results, string Error)>
            SearchEmployeesAsync(string query)
        {
            EnsureCloudflareHeaders();

            const string path = "/api/employees/search";
            const string method = "GET";

            byte[] bodyBytes = Array.Empty<byte>(); // no body for GET

            string url = BASE_URL + path + "?q=" + Uri.EscapeDataString(query);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddSignedHeaders(request, bodyBytes, method, path);

            try
            {
                var response = await _httpClient.SendAsync(request);
                var respBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string err = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {respBody}";
                    ClientLogger.Log($"SearchEmployeesAsync failed: {err}");
                    return (false, new List<EmployeeSearchResult>(), err);
                }

                var list = JsonConvert.DeserializeObject<List<EmployeeSearchResult>>(respBody)
                           ?? new List<EmployeeSearchResult>();

                return (true, list, null);
            }
            catch (TaskCanceledException tex)
            {
                ClientLogger.Log("SearchEmployeesAsync timeout", tex);
                return (false, new List<EmployeeSearchResult>(), "Request timed out waiting for server.");
            }
            catch (Exception ex)
            {
                ClientLogger.Log("SearchEmployeesAsync threw exception", ex);
                return (false, new List<EmployeeSearchResult>(), ex.Message);
            }
        }

        // ---------- ENROL / RE-ENROL / DELETE ----------

        public class EnrolRequestDto
        {
            public string siteId { get; set; }
            public string deviceId { get; set; }
            public string employeeName { get; set; }
            public string templateBase64 { get; set; }
            public string clientLocalTime { get; set; }
        }

        public class EnrolResponseDto
        {
            public int enrollmentId { get; set; }
            public string enrollmentIdFormatted { get; set; }
            public string employeeRef { get; set; }
            public string status { get; set; }
        }

        public class ReEnrolRequestDto
        {
            public int enrollmentId { get; set; }
            public string templateBase64 { get; set; }
            public string clientLocalTime { get; set; }
        }

        /// <summary>
        /// First-time enrolment: POST /api/enrol
        /// </summary>
        public async Task<(bool Success, EnrolResponseDto Response, string Error)> EnrolAsync(
            string siteId,
            string deviceId,
            string employeeName,
            string templateBase64)
        {
            EnsureCloudflareHeaders();

            var payload = new EnrolRequestDto
            {
                siteId = siteId,
                deviceId = deviceId,
                employeeName = employeeName,
                templateBase64 = templateBase64,
                clientLocalTime = DateTime.Now.ToString("o")
            };

            string json = JsonConvert.SerializeObject(payload);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(json);

            const string path = "/api/enrol";

            var req = new HttpRequestMessage(HttpMethod.Post, BASE_URL + path)
            {
                Content = new ByteArrayContent(bodyBytes)
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            AddSignedHeaders(req, bodyBytes, "POST", path);

            try
            {
                var resp = await _httpClient.SendAsync(req);
                var respBody = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    string err = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {respBody}";
                    return (false, null, err);
                }

                var dto = JsonConvert.DeserializeObject<EnrolResponseDto>(respBody);
                if (dto == null)
                {
                    return (false, null, "Empty/invalid JSON response.");
                }

                return (true, dto, null);
            }
            catch (TaskCanceledException tex)
            {
                ClientLogger.Log("EnrolAsync timeout", tex);
                return (false, null, "Request timed out waiting for server.");
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        /// <summary>
        /// Re-enrolment: POST /api/reenrol
        /// </summary>
        public async Task<(bool Success, EnrolResponseDto Response, string Error)> ReEnrolAsync(
            int enrollmentId,
            string templateBase64)
        {
            EnsureCloudflareHeaders();

            var payload = new ReEnrolRequestDto
            {
                enrollmentId = enrollmentId,
                templateBase64 = templateBase64,
                clientLocalTime = DateTime.Now.ToString("o")
            };

            string json = JsonConvert.SerializeObject(payload);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(json);

            const string path = "/api/reenrol";

            var req = new HttpRequestMessage(HttpMethod.Post, BASE_URL + path)
            {
                Content = new ByteArrayContent(bodyBytes)
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            AddSignedHeaders(req, bodyBytes, "POST", path);

            try
            {
                var resp = await _httpClient.SendAsync(req);
                var respBody = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    string err = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {respBody}";
                    return (false, null, err);
                }

                var dto = JsonConvert.DeserializeObject<EnrolResponseDto>(respBody);
                if (dto == null)
                {
                    return (false, null, "Empty/invalid JSON response.");
                }

                return (true, dto, null);
            }
            catch (TaskCanceledException tex)
            {
                ClientLogger.Log("ReEnrolAsync timeout", tex);
                return (false, null, "Request timed out waiting for server.");
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        /// <summary>
        /// Delete enrollment and templates on server: DELETE /api/employees/{id}
        /// </summary>
        public async Task<(bool Success, string Error)> DeleteEnrollmentAsync(int enrollmentId)
        {
            EnsureCloudflareHeaders();

            string path = $"/api/employees/{enrollmentId}";
            const string method = "DELETE";

            byte[] bodyBytes = Array.Empty<byte>(); // no body

            var req = new HttpRequestMessage(HttpMethod.Delete, BASE_URL + path);
            AddSignedHeaders(req, bodyBytes, method, path);

            try
            {
                var resp = await _httpClient.SendAsync(req);
                var respBody = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    string err = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {respBody}";
                    ClientLogger.Log($"DeleteEnrollmentAsync failed: {err}");
                    return (false, err);
                }

                return (true, null);
            }
            catch (TaskCanceledException tex)
            {
                ClientLogger.Log("DeleteEnrollmentAsync timeout", tex);
                return (false, "Request timed out waiting for server.");
            }
            catch (Exception ex)
            {
                ClientLogger.Log("DeleteEnrollmentAsync threw exception", ex);
                return (false, ex.Message);
            }
        }

        // ---------- SCAN (NO SITE/DEVICE FROM CLIENT) ----------

        public class ScanRequestDto
        {
            public int enrollmentId { get; set; }
            public double confidence { get; set; }
            public string employeeName { get; set; }
            public string clientLocalTime { get; set; }
        }

        public class ScanResponseDto
        {
            public string action { get; set; }
        }

        /// <summary>
        /// Record a scan event: POST /api/scan
        /// SiteId / DeviceId are filled in by the server.
        /// </summary>
        public async Task<(bool Success, ScanResponseDto Response, string Error)> ScanAsync(
            int enrollmentId,
            double confidence,
            string employeeName)
        {
            EnsureCloudflareHeaders();

            var payload = new ScanRequestDto
            {
                enrollmentId = enrollmentId,
                confidence = confidence,
                employeeName = employeeName,
                clientLocalTime = DateTime.Now.ToString("o")
            };

            string json = JsonConvert.SerializeObject(payload);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(json);

            const string path = "/api/scan";

            var req = new HttpRequestMessage(HttpMethod.Post, BASE_URL + path)
            {
                Content = new ByteArrayContent(bodyBytes)
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            AddSignedHeaders(req, bodyBytes, "POST", path);

            try
            {
                var resp = await _httpClient.SendAsync(req);
                var respBody = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    string err = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {respBody}";
                    ClientLogger.Log($"ScanAsync failed: {err}");
                    return (false, null, err);
                }

                var dto = JsonConvert.DeserializeObject<ScanResponseDto>(respBody);
                if (dto == null)
                {
                    return (false, null, "Empty/invalid JSON response.");
                }

                return (true, dto, null);
            }
            catch (TaskCanceledException tex)
            {
                ClientLogger.Log("ScanAsync timeout", tex);
                return (false, null, "Request timed out waiting for server.");
            }
            catch (Exception ex)
            {
                ClientLogger.Log("ScanAsync threw exception", ex);
                return (false, null, ex.Message);
            }
        }

        // ---------- TEMPLATE FETCH ----------

        public async Task<(bool Success, string TemplateBase64, string Error)> GetTemplateAsync(int id)
        {
            EnsureCloudflareHeaders();

            string path = $"/api/template/{id}";
            byte[] bodyBytes = Array.Empty<byte>();

            var req = new HttpRequestMessage(HttpMethod.Get, BASE_URL + path);
            AddSignedHeaders(req, bodyBytes, "GET", path);

            try
            {
                var resp = await _httpClient.SendAsync(req);
                var respBody = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    string err = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {respBody}";
                    ClientLogger.Log($"GetTemplateAsync failed: {err}");
                    return (false, null, err);
                }

                dynamic obj = JsonConvert.DeserializeObject(respBody);
                string templateBase64 = (string)obj.templateBase64;
                return (true, templateBase64, null);
            }
            catch (TaskCanceledException tex)
            {
                ClientLogger.Log("GetTemplateAsync timeout", tex);
                return (false, null, "Request timed out waiting for server.");
            }
            catch (Exception ex)
            {
                ClientLogger.Log("GetTemplateAsync threw exception", ex);
                return (false, null, ex.Message);
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
