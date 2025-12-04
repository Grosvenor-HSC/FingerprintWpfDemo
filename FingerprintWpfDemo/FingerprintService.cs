using System;
using System.Collections.Generic;
using System.IO;            // Needed for File/Path
using DPUruNet;

namespace FingerprintWpfDemo
{
    public class FingerprintService : IDisposable
    {
        private Reader _reader;
        private bool _initialized;
        public string LastError { get; private set; }

        private readonly Dictionary<string, Fmd> _templates =
            new Dictionary<string, Fmd>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _enrollmentIds =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private const int MATCH_THRESHOLD = 100;

        // Local template storage directory
        private readonly string _templateDir;

        public FingerprintService()
        {
            LastError = string.Empty;

            _templateDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "templates");

            if (!Directory.Exists(_templateDir))
                Directory.CreateDirectory(_templateDir);
        }

        // ------------------------------------------------------------
        // INITIALIZE READER
        // ------------------------------------------------------------
        public bool Initialize()
        {
            try
            {
                ReaderCollection readers = ReaderCollection.GetReaders();
                if (readers == null || readers.Count == 0)
                {
                    LastError = "No fingerprint readers found.";
                    return false;
                }

                _reader = readers[0];

                var rc = _reader.Open(Constants.CapturePriority.DP_PRIORITY_EXCLUSIVE);
                if (rc != Constants.ResultCode.DP_SUCCESS)
                {
                    rc = _reader.Open(Constants.CapturePriority.DP_PRIORITY_COOPERATIVE);
                    if (rc != Constants.ResultCode.DP_SUCCESS)
                    {
                        LastError = "Failed to open reader: " + rc;
                        _reader = null;
                        return false;
                    }
                }

                _initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _reader = null;
                _initialized = false;
                return false;
            }
        }

        // ------------------------------------------------------------
        // INTERNAL STATE MANAGEMENT
        // ------------------------------------------------------------

        public bool HasTemplate(string name) => _templates.ContainsKey(name);

        public IEnumerable<string> GetUserNames() => _templates.Keys;

        public bool RemoveTemplate(string name)
        {
            bool removed = _templates.Remove(name);

            // delete local file too
            string path = Path.Combine(_templateDir, name + ".fpt");
            if (File.Exists(path)) File.Delete(path);

            _enrollmentIds.Remove(name);
            return removed;
        }

        public void SetEnrollmentId(string name, int enrollmentId)
        {
            _enrollmentIds[name] = enrollmentId;
        }

        public int? GetEnrollmentId(string name)
        {
            if (_enrollmentIds.TryGetValue(name, out var id))
                return id;
            return null;
        }

        // ------------------------------------------------------------
        // LOCAL TEMPLATE STORAGE
        // ------------------------------------------------------------

        /// <summary>
        /// Save raw ANSI FMD bytes to disk and into memory (_templates).
        /// These bytes must be the same as Fmd.Bytes produced by
        /// Enrollment.CreateEnrollmentFmd(Constants.Formats.Fmd.ANSI, ...).
        /// </summary>
        public void SaveTemplate(string name, byte[] templateBytes)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name cannot be empty.", nameof(name));

            if (templateBytes == null || templateBytes.Length == 0)
                throw new ArgumentException("Template bytes cannot be empty.", nameof(templateBytes));

            // 1) Persist raw template bytes to disk
            string fullPath = Path.Combine(_templateDir, name + ".fpt");
            File.WriteAllBytes(fullPath, templateBytes);

            // 2) Load into memory as an FMD so Verify() can use it immediately
            try
            {
                // Your SDK’s constructor signature is Fmd(byte[] bytes, int format, string version)
                // We know the format is ANSI; version is typically "1.0.0".
                var fmd = new Fmd(templateBytes, (int)Constants.Formats.Fmd.ANSI, "1.0.0");

                _templates[name] = fmd;
            }
            catch (Exception ex)
            {
                LastError = "Failed to hydrate FMD for '" + name + "': " + ex.Message;
            }
        }

        public byte[] LoadTemplate(string name, Action<string> log = null)
        {
            string file = Path.Combine(_templateDir, name + ".fpt");

            if (!File.Exists(file))
            {
                log?.Invoke($"No local template file found for '{name}'.");
                return null;
            }

            try
            {
                return File.ReadAllBytes(file);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Failed reading local template for '{name}': {ex.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------
        // GET TEMPLATE BYTES (FOR SENDING TO SERVER)
        // ------------------------------------------------------------
        public byte[] GetTemplateBytes(string name, Action<string> log)
        {
            if (_templates.TryGetValue(name, out Fmd fmd))
            {
                try
                {
                    return fmd.Bytes;
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Failed to access template bytes: {ex.Message}");
                    return null;
                }
            }

            // Fallback to disk (used for downloaded templates)
            return LoadTemplate(name, log);
        }

        // ------------------------------------------------------------
        // ENROLMENT
        // ------------------------------------------------------------
        public bool Enrol(string name, Action<string> log)
        {
            if (!_initialized || _reader == null)
            {
                log("Reader not initialized.");
                return false;
            }

            var samples = new List<Fmd>();

            while (samples.Count < 4)
            {
                log($"[{name}] Scan {samples.Count + 1} of 4 – place the SAME finger.");
                var fmd = CaptureFmd(log);
                if (fmd == null)
                {
                    log("Capture failed, repeating this scan.");
                    continue;
                }
                samples.Add(fmd);
            }

            log("Creating enrollment template from the 4 scans...");

            DataResult<Fmd> result =
                Enrollment.CreateEnrollmentFmd(Constants.Formats.Fmd.ANSI, samples);

            if (result.ResultCode == Constants.ResultCode.DP_SUCCESS && result.Data != null)
            {
                _templates[name] = result.Data;
                log("Enrollment FMD created successfully.");
                return true;
            }

            log("Enrollment failed. Result: " + result.ResultCode);
            return false;
        }

        // ------------------------------------------------------------
        // VERIFY (single user)
        // ------------------------------------------------------------
        public (bool success, int score, double confidence) Verify(string name, Action<string> log)
        {
            if (!_initialized || _reader == null)
            {
                log("Reader not initialized.");
                return (false, int.MaxValue, 0);
            }

            if (!_templates.TryGetValue(name, out var enrolled))
            {
                log($"No template enrolled for '{name}'.");
                return (false, int.MaxValue, 0);
            }

            log($"Scan finger for '{name}' to verify...");
            var probe = CaptureFmd(log);
            if (probe == null)
            {
                log("Capture failed, cannot verify.");
                return (false, int.MaxValue, 0);
            }

            CompareResult cr = Comparison.Compare(probe, 0, enrolled, 0);
            if (cr.ResultCode != Constants.ResultCode.DP_SUCCESS)
            {
                log("Compare failed: " + cr.ResultCode);
                return (false, int.MaxValue, 0);
            }

            int score = cr.Score;
            log($"Compare score with {name}: {score}");

            if (score <= MATCH_THRESHOLD)
            {
                double confidence = 1.0 - Math.Min(score, MATCH_THRESHOLD) / (double)MATCH_THRESHOLD;
                return (true, score, confidence);
            }

            return (false, score, 0);
        }

        // ------------------------------------------------------------
        // CAPTURE FROM READER
        // ------------------------------------------------------------
        private Fmd CaptureFmd(Action<string> log)
        {
            if (_reader == null)
            {
                log("No reader.");
                return null;
            }

            try
            {
                CaptureResult captureResult = _reader.Capture(
                    Constants.Formats.Fid.ANSI,
                    Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT,
                    5000,
                    _reader.Capabilities.Resolutions[0]);

                if (captureResult.ResultCode != Constants.ResultCode.DP_SUCCESS)
                {
                    log("Capture failed. Result: " + captureResult.ResultCode);
                    return null;
                }

                if (captureResult.Data == null || captureResult.Data.Views.Count == 0)
                {
                    log("Capture returned no data.");
                    return null;
                }

                log("Capture OK. Quality: " + captureResult.Quality);

                DataResult<Fmd> conversionResult =
                    FeatureExtraction.CreateFmdFromFid(captureResult.Data, Constants.Formats.Fmd.ANSI);

                if (conversionResult.ResultCode != Constants.ResultCode.DP_SUCCESS ||
                    conversionResult.Data == null)
                {
                    log("CreateFmdFromFid failed. Result: " + conversionResult.ResultCode);
                    return null;
                }

                return conversionResult.Data;
            }
            catch (Exception ex)
            {
                log("Capture exception: " + ex.Message);
                return null;
            }
        }

        // ------------------------------------------------------------
        // DISPOSE
        // ------------------------------------------------------------
        public void Dispose()
        {
            try { _reader?.Dispose(); }
            catch { }
        }
    }
}
