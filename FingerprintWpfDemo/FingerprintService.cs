using System;
using System.Collections.Generic;
using DPUruNet;

namespace FingerprintWpfDemo
{
    public class FingerprintService : IDisposable
    {
        private Reader _reader;
        private bool _initialized;

        // Name -> Template
        private readonly Dictionary<string, Fmd> _templates =
            new Dictionary<string, Fmd>(StringComparer.OrdinalIgnoreCase);

        private const int MATCH_THRESHOLD = 100;

        public IReadOnlyDictionary<string, Fmd> Templates => _templates;
        public bool IsInitialized => _initialized;

        public string LastError { get; private set; }

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

                var openResult = _reader.Open(Constants.CapturePriority.DP_PRIORITY_EXCLUSIVE);
                if (openResult != Constants.ResultCode.DP_SUCCESS)
                {
                    openResult = _reader.Open(Constants.CapturePriority.DP_PRIORITY_COOPERATIVE);
                }

                if (openResult != Constants.ResultCode.DP_SUCCESS)
                {
                    LastError = $"Failed to open reader: {openResult}";
                    _reader = null;
                    return false;
                }

                _initialized = true;
                return true;
            }
            catch (SDKException ex)
            {
                LastError = "SDK exception: " + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                LastError = "Exception: " + ex.Message;
                return false;
            }
        }

        public bool HasTemplate(string name) => _templates.ContainsKey(name);

        public bool RemoveTemplate(string name) => _templates.Remove(name);

        // ---------------- ENROL ----------------

        public bool Enrol(string name, Action<string> log)
        {
            if (!_initialized)
            {
                log("Reader not initialized.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                log("Name is empty.");
                return false;
            }

            var samples = new List<Fmd>();

            for (int i = 0; i < 4;)
            {
                log($"[{name}] Scan {i + 1} of 4 – place the SAME finger.");
                var fmd = CaptureFmd(log);
                if (fmd == null)
                {
                    log("Capture failed, repeating this scan.");
                    continue;
                }

                samples.Add(fmd);
                i++;
            }

            log("Creating enrollment template from 4 scans...");

            DataResult<Fmd> enrollmentResult =
                Enrollment.CreateEnrollmentFmd(Constants.Formats.Fmd.ANSI, samples);

            if (enrollmentResult.ResultCode == Constants.ResultCode.DP_SUCCESS &&
                enrollmentResult.Data != null)
            {
                _templates[name] = enrollmentResult.Data;
                log("Enrollment FMD created successfully.");
                return true;
            }

            log($"Enrollment failed: {enrollmentResult.ResultCode}");
            return false;
        }

        // ---------------- IDENTIFY ----------------

        public (bool success, string bestName, int bestScore, double confidence)
            Identify(Action<string> log)
        {
            if (!_initialized)
            {
                log("Reader not initialized.");
                return (false, null, int.MaxValue, 0);
            }

            if (_templates.Count == 0)
            {
                log("No templates enrolled.");
                return (false, null, int.MaxValue, 0);
            }

            log("Scan a finger to identify...");
            var probe = CaptureFmd(log);
            if (probe == null)
            {
                log("Capture failed, cannot identify.");
                return (false, null, int.MaxValue, 0);
            }

            string bestName = null;
            int bestScore = int.MaxValue;

            foreach (var kvp in _templates)
            {
                string name = kvp.Key;
                Fmd enrolled = kvp.Value;

                CompareResult cr = Comparison.Compare(probe, 0, enrolled, 0);
                if (cr.ResultCode != Constants.ResultCode.DP_SUCCESS)
                {
                    log($"Compare failed for {name}: {cr.ResultCode}");
                    continue;
                }

                int score = cr.Score;
                log($"Compare score with {name}: {score}");

                if (score < bestScore)
                {
                    bestScore = score;
                    bestName = name;
                }
            }

            if (bestName != null && bestScore <= MATCH_THRESHOLD)
            {
                double confidence = 1.0 - Math.Min(bestScore, MATCH_THRESHOLD) / (double)MATCH_THRESHOLD;
                if (confidence < 0) confidence = 0;
                if (confidence > 1) confidence = 1;

                log($"Match: {bestName} (score {bestScore}, ~{confidence * 100:0}% confidence)");
                return (true, bestName, bestScore, confidence);
            }
            else
            {
                log("No matching template found.");
                return (false, null, bestScore, 0);
            }
        }

        // ---------------- VERIFY ONE USER BY NAME ----------------

        public (bool success, int score, double confidence) Verify(string name, Action<string> log)
        {
            if (!_initialized)
            {
                log("Reader not initialized.");
                return (false, int.MaxValue, 0);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                log("Name is empty.");
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
                log($"Compare failed: {cr.ResultCode}");
                return (false, int.MaxValue, 0);
            }

            int score = cr.Score;
            log($"Compare score with {name}: {score}");

            if (score <= MATCH_THRESHOLD)
            {
                double confidence = 1.0 - Math.Min(score, MATCH_THRESHOLD) / (double)MATCH_THRESHOLD;
                if (confidence < 0) confidence = 0;
                if (confidence > 1) confidence = 1;

                log($"Fingerprint matches template for '{name}' (~{confidence * 100:0}% confidence).");
                return (true, score, confidence);
            }
            else
            {
                log("Fingerprint does NOT match the stored template.");
                return (false, score, 0);
            }
        }

        // ---------------- CAPTURE ----------------

        private Fmd CaptureFmd(Action<string> log)
        {
            try
            {
                log("Place finger on the reader...");

                CaptureResult captureResult = _reader.Capture(
                    Constants.Formats.Fid.ANSI,
                    Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT,
                    5000,
                    _reader.Capabilities.Resolutions[0]
                );

                if (captureResult.ResultCode != Constants.ResultCode.DP_SUCCESS)
                {
                    log($"Capture failed: {captureResult.ResultCode}");
                    return null;
                }

                if (captureResult.Data == null || captureResult.Data.Views.Count == 0)
                {
                    log("Capture returned no data.");
                    return null;
                }

                log($"Capture OK. Quality: {captureResult.Quality}");

                DataResult<Fmd> fmdResult =
                    FeatureExtraction.CreateFmdFromFid(captureResult.Data, Constants.Formats.Fmd.ANSI);

                if (fmdResult.ResultCode != Constants.ResultCode.DP_SUCCESS ||
                    fmdResult.Data == null)
                {
                    log($"CreateFmdFromFid failed: {fmdResult.ResultCode}");
                    return null;
                }

                return fmdResult.Data;
            }
            catch (SDKException ex)
            {
                log("SDK exception during capture: " + ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                log("Exception during capture: " + ex.Message);
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                _reader?.Dispose();
            }
            catch { }
        }
    }
}
