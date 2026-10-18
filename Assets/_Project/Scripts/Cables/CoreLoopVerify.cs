using System.Globalization;
using System.Text;
using PowerCutCrew.Fuel;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Cables
{
    /// <summary>
    /// Bot-run assertion for the core loop (see <see cref="NetVerifyExtras"/>): a client reports the generator state / fuel / heat,
    /// the fuel of every can and the state of every cable it sees; the host compares with its own values. Values that move
    /// all the time (fuel, heat) get a tolerance, discrete states must match once the host has been quiet for a few seconds.
    /// </summary>
    public class CoreLoopVerify : MonoBehaviour
    {
        private const float QuietSeconds = 3f;
        private static float _changedAt;
        private static string _signature = "";
        private static readonly RateTracker HeatRate = new RateTracker();
        private static readonly RateTracker FuelRate = new RateTracker();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (!LaunchArgs.Verify) { return; }
            NetVerifyExtras.Add(BuildReport, HostCheck);
            var go = new GameObject("CoreLoopVerify") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<CoreLoopVerify>();
        }

        private static string F(float v) => v.ToString("F1", CultureInfo.InvariantCulture);

        private static string BuildReport()
        {
            GeneratorNet gen = GeneratorNet.Instance;
            var sb = new StringBuilder();
            if (gen != null) { sb.Append($"gen={gen.State},{F(gen.Fuel)},{F(gen.Heat)}"); }
            sb.Append(";cans=");
            foreach (NetworkPhysicsItem item in NetworkPhysicsItem.All)
            {
                var can = item.GetComponent<FuelCan>();
                if (can != null && item.Index >= 0) { sb.Append($"{item.Index}:{F(can.Amount)}|"); }
            }
            sb.Append(";cables=");
            foreach (CableNet c in CableNet.All) { sb.Append($"{c.NetworkObjectId}:{c.State}:{c.SocketId}|"); }
            return sb.ToString();
        }

        private static string Part(string report, string key)
        {
            foreach (string part in report.Split(';'))
            {
                if (part.StartsWith(key + "=")) { return part.Substring(key.Length + 1); }
            }
            return "";
        }

        private static string HostCheck(string report)
        {
            if (Time.time - _changedAt < QuietSeconds) { return "skip"; }
            var sb = new StringBuilder();
            GeneratorNet gen = GeneratorNet.Instance;
            string[] g = Part(report, "gen").Split(',');
            if (gen != null && g.Length == 3)
            {
                float fuel = float.Parse(g[1], CultureInfo.InvariantCulture);
                float heat = float.Parse(g[2], CultureInfo.InvariantCulture);
                if (g[0] != gen.State.ToString()) { sb.Append($"generator state {g[0]} vs host {gen.State}; "); }
                // heat and fuel keep moving: the tolerance grows with how fast they changed during the age of the client report
                NetTuning nt = NetTuning.Instance;
                float fuelTolerance = VerifyTolerance.For(nt.VerifyFuelBase, FuelRate.MaxAbsRate(Time.time), nt.VerifySnapshotAgeSeconds);
                float heatTolerance = VerifyTolerance.For(nt.VerifyHeatBase, HeatRate.MaxAbsRate(Time.time), nt.VerifySnapshotAgeSeconds);
                if (Mathf.Abs(fuel - gen.Fuel) > fuelTolerance) { sb.Append($"generator fuel {fuel:F1} vs host {gen.Fuel:F1} (tolerance {fuelTolerance:F1}); "); }
                if (Mathf.Abs(heat - gen.Heat) > heatTolerance) { sb.Append($"generator heat {heat:F0} vs host {gen.Heat:F0} (tolerance {heatTolerance:F1}); "); }
            }
            foreach (string entry in Part(report, "cans").Split('|'))
            {
                string[] kv = entry.Split(':');
                if (kv.Length != 2) { continue; }
                int index = int.Parse(kv[0], CultureInfo.InvariantCulture);
                float amount = float.Parse(kv[1], CultureInfo.InvariantCulture);
                foreach (NetworkPhysicsItem item in NetworkPhysicsItem.All)
                {
                    var can = item.GetComponent<FuelCan>();
                    if (can != null && item.Index == index && Mathf.Abs(can.Amount - amount) > 1.5f) { sb.Append($"can {index} fuel {amount:F1} vs host {can.Amount:F1}; "); }
                }
            }
            foreach (string entry in Part(report, "cables").Split('|'))
            {
                string[] kv = entry.Split(':');
                if (kv.Length != 3) { continue; }
                ulong id = ulong.Parse(kv[0], CultureInfo.InvariantCulture);
                CableNet host = null;
                foreach (CableNet c in CableNet.All)
                {
                    if (c.NetworkObjectId == id) { host = c; break; }
                }
                if (host == null) { continue; }   // despawned on the host while the report was on its way
                if (kv[1] != host.State.ToString() || kv[2] != host.SocketId.ToString()) { sb.Append($"cable {id} {kv[1]}/{kv[2]} vs host {host.State}/{host.SocketId}; "); }
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        /// <summary>Host: remember when something discrete last changed, so a comparison only happens while the world is quiet.</summary>
        private void Update()
        {
            GeneratorNet gen = GeneratorNet.Instance;
            if (gen == null || !gen.IsServer) { return; }
            HeatRate.Add(Time.time, gen.Heat);
            FuelRate.Add(Time.time, gen.Fuel);
            var sb = new StringBuilder();
            sb.Append(gen.State);
            foreach (CableNet c in CableNet.All) { sb.Append($"|{c.Outlet}:{c.State}:{c.SocketId}:{c.GetInstanceID()}"); }
            sb.Append(HoldRegistry.Instance != null ? HoldRegistry.Instance.ActiveCount : 0);
            string signature = sb.ToString();
            if (signature != _signature)
            {
                _signature = signature;
                _changedAt = Time.time;
            }
        }
    }
}
