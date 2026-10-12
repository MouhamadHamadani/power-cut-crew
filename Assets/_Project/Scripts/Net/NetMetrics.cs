using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// Run metrics for bot runs: frame time, errors, traffic, counters and value statistics.
    /// Writes Logs/net-metrics-&lt;role&gt;-&lt;pid&gt;.json on quit or after -duration seconds.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class NetMetrics : MonoBehaviour
    {
        private double _frameStart;
        private bool _headless;
        private class Stat
        {
            public double Sum;
            public double Max;
            public long Count;
        }

        public static NetMetrics Instance { get; private set; }

        public static float RttMs;

        private static long _sentBytes, _recvBytes, _sentMsgs, _recvMsgs;
        private static readonly Dictionary<string, int> _counters = new Dictionary<string, int>();
        private static readonly Dictionary<string, Stat> _stats = new Dictionary<string, Stat>();
        private static readonly object _lock = new object();

        private float _start, _frameSum, _framePeak;
        private int _frames, _errors, _exceptions;
        private float _bucketTime;
        private long _bucketSent, _bucketRecv;
        private double _peakUp, _peakDown, _rttSum;
        private int _rttCount;
        private bool _written;

        public static float KbpsUp { get; private set; }
        public static float KbpsDown { get; private set; }

        public static void AddSent(int bytes)
        {
            lock (_lock) { _sentBytes += bytes; _sentMsgs++; }
        }

        public static void AddRecv(int bytes)
        {
            lock (_lock) { _recvBytes += bytes; _recvMsgs++; }
        }

        public static void Count(string key)
        {
            lock (_lock) { _counters[key] = (_counters.TryGetValue(key, out int v) ? v : 0) + 1; }
        }

        /// <summary>Record a sample of a named value (reported as avg / max / count).</summary>
        public static void Sample(string key, double value)
        {
            lock (_lock)
            {
                if (!_stats.TryGetValue(key, out Stat s)) { _stats[key] = s = new Stat(); }
                s.Sum += value;
                s.Max = Math.Max(s.Max, value);
                s.Count++;
            }
        }

        public static void Ensure()
        {
            if (Instance == null) { new GameObject("NetMetrics").AddComponent<NetMetrics>(); }
        }

        private void Awake()
        {
            Instance = this;
            _headless = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
            _start = Time.realtimeSinceStartup;
            Application.logMessageReceivedThreaded += OnLog;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
        }

        private void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Exception) { _exceptions++; }
            else if (type == LogType.Error || type == LogType.Assert) { _errors++; }
        }

        /// <summary>Headless players ignore targetFrameRate, so cap them at 60 fps by hand to keep per-frame metrics comparable.</summary>
        private void LateUpdate()
        {
            if (!_headless) { return; }
            double spent = Time.realtimeSinceStartupAsDouble - _frameStart;
            if (spent < 1.0 / 60.0) { System.Threading.Thread.Sleep((int)((1.0 / 60.0 - spent) * 1000.0)); }
        }

        private void Update()
        {
            _frameStart = Time.realtimeSinceStartupAsDouble;
            float dt = Time.unscaledDeltaTime;
            _frames++;
            _frameSum += dt;
            _framePeak = Mathf.Max(_framePeak, dt);
            _bucketTime += dt;
            if (_bucketTime >= 1f)
            {
                long s, r;
                lock (_lock) { s = _sentBytes; r = _recvBytes; }
                KbpsUp = (float)((s - _bucketSent) * 8.0 / 1000.0 / _bucketTime);
                KbpsDown = (float)((r - _bucketRecv) * 8.0 / 1000.0 / _bucketTime);
                _peakUp = Math.Max(_peakUp, KbpsUp);
                _peakDown = Math.Max(_peakDown, KbpsDown);
                _bucketSent = s;
                _bucketRecv = r;
                _bucketTime = 0;
                if (RttMs > 0) { _rttSum += RttMs; _rttCount++; }
            }
            float duration = LaunchArgs.Duration;
            if (duration > 0 && Time.realtimeSinceStartup - _start >= duration)
            {
                Write();
                Application.Quit();
            }
        }

        private void OnApplicationQuit()
        {
            Write();
        }

        public void Write()
        {
            if (_written) { return; }
            _written = true;
            float elapsed = Mathf.Max(0.001f, Time.realtimeSinceStartup - _start);
            var ci = CultureInfo.InvariantCulture;
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"role\": \"{LaunchArgs.Role}\",\n  \"pid\": {pid},\n  \"netsim\": \"{LaunchArgs.NetSim}\",\n  \"bot\": {LaunchArgs.Bot.ToString().ToLowerInvariant()},\n");
            sb.Append($"  \"seconds\": {elapsed.ToString("F1", ci)},\n");
            lock (_lock)
            {
                sb.Append($"  \"avgKbpsUp\": {(_sentBytes * 8.0 / 1000.0 / elapsed).ToString("F2", ci)},\n  \"avgKbpsDown\": {(_recvBytes * 8.0 / 1000.0 / elapsed).ToString("F2", ci)},\n");
                sb.Append($"  \"peakKbpsUp\": {_peakUp.ToString("F2", ci)},\n  \"peakKbpsDown\": {_peakDown.ToString("F2", ci)},\n");
                foreach (KeyValuePair<string, int> kv in _counters) { sb.Append($"  \"count_{kv.Key}\": {kv.Value},\n"); }
                foreach (KeyValuePair<string, Stat> kv in _stats)
                {
                    sb.Append($"  \"{kv.Key}_avg\": {(kv.Value.Sum / Math.Max(1, kv.Value.Count)).ToString("F4", ci)},\n");
                    sb.Append($"  \"{kv.Key}_max\": {kv.Value.Max.ToString("F4", ci)},\n  \"{kv.Key}_n\": {kv.Value.Count},\n");
                }
            }
            sb.Append($"  \"avgFrameMs\": {(_frameSum / Mathf.Max(1, _frames) * 1000f).ToString("F2", ci)},\n  \"peakFrameMs\": {(_framePeak * 1000f).ToString("F1", ci)},\n");
            sb.Append($"  \"avgRttMs\": {(_rttCount > 0 ? _rttSum / _rttCount : 0).ToString("F1", ci)},\n");
            sb.Append($"  \"errors\": {_errors},\n  \"exceptions\": {_exceptions}\n}}\n");
            string dir = Path.Combine(Application.dataPath, "..", "Logs");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"net-metrics-{LaunchArgs.Role}-{pid}.json"), sb.ToString());
        }
    }
}
