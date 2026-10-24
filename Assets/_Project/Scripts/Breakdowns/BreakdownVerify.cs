using System.Globalization;
using System.Text;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>
    /// Bot-run assertion for the breakdowns (see <see cref="NetVerifyExtras"/>): a client reports which breakdowns it sees, the sweet spot, the hits done, the tripped outlet,
    /// the lever and the two fix progress bars; the host compares with its own values, field by field. A field the host changed in the last moment is skipped on its own.
    /// This is what a late joiner has to get right: whatever is broken right now, with its sweet spot, its progress and its tripped outlet.
    /// </summary>
    public class BreakdownVerify : MonoBehaviour
    {
        private const float QuietSeconds = 1.5f;
        private const int ProgressTolerance = 15;   // percent: the bars move in 5 % steps while somebody holds

        private static readonly ChangeClock Clock = new ChangeClock();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (!LaunchArgs.Verify) { return; }
            NetVerifyExtras.AddFields(BuildReport, HostCheck);
            var go = new GameObject("BreakdownVerify") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<BreakdownVerify>();
        }

        private static string BuildReport()
        {
            BreakdownNet net = BreakdownNet.Instance;
            if (net == null || !net.IsSpawned) { return string.Empty; }
            var sb = new StringBuilder();
            sb.Append("brk=").Append(net.ActiveMask).Append(',').Append(net.SweetSpotIndex).Append(',').Append(net.HitsDone).Append(',').Append(net.TrippedOutlet).Append(',')
                .Append(net.LeverUp ? 1 : 0).Append(',').Append(Mathf.RoundToInt(net.BeltProgress01 * 100f)).Append(',').Append(Mathf.RoundToInt(net.ResetProgress01 * 100f));
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

        private static bool Changing(string key) => Clock.Changing(key, Time.time, QuietSeconds);

        private static void HostCheck(string report, VerifyFields v)
        {
            BreakdownNet net = BreakdownNet.Instance;
            string[] f = Part(report, "brk").Split(',');
            if (net == null || !net.IsSpawned || f.Length != 7) { return; }
            int mask = int.Parse(f[0], CultureInfo.InvariantCulture);
            int spot = int.Parse(f[1], CultureInfo.InvariantCulture);
            int hits = int.Parse(f[2], CultureInfo.InvariantCulture);
            int outlet = int.Parse(f[3], CultureInfo.InvariantCulture);
            int lever = int.Parse(f[4], CultureInfo.InvariantCulture);
            int belt = int.Parse(f[5], CultureInfo.InvariantCulture);
            int reset = int.Parse(f[6], CultureInfo.InvariantCulture);
            v.Field("brk.active", Changing("brk.active"), mask == net.ActiveMask, $"breakdowns active {mask} vs host {net.ActiveMask}");
            bool sputter = BreakdownTypes.Has(net.ActiveMask, BreakdownType.Sputter);
            v.Field("brk.spot", Changing("brk.active") || Changing("brk.spot"), !sputter || spot == net.SweetSpotIndex, $"sweet spot {spot} vs host {net.SweetSpotIndex}");
            v.Field("brk.hits", Changing("brk.active") || Changing("brk.hits"), !sputter || hits == net.HitsDone, $"sputter hits {hits} vs host {net.HitsDone}");
            bool breaker = BreakdownTypes.Has(net.ActiveMask, BreakdownType.Breaker);
            v.Field("brk.outlet", Changing("brk.active") || Changing("brk.outlet"), !breaker || outlet == net.TrippedOutlet, $"tripped outlet {outlet} vs host {net.TrippedOutlet}");
            v.Field("brk.lever", Changing("brk.active") || Changing("brk.lever"), lever == (net.LeverUp ? 1 : 0), $"lever {lever} vs host {(net.LeverUp ? 1 : 0)}");
            int hostBelt = Mathf.RoundToInt(net.BeltProgress01 * 100f);
            int hostReset = Mathf.RoundToInt(net.ResetProgress01 * 100f);
            v.Field("brk.belt", Changing("brk.belt"), Mathf.Abs(belt - hostBelt) <= ProgressTolerance, $"belt fit {belt} % vs host {hostBelt} %");
            v.Field("brk.reset", Changing("brk.reset"), Mathf.Abs(reset - hostReset) <= ProgressTolerance, $"breaker reset {reset} % vs host {hostReset} %");
        }

        /// <summary>Host: note when each value last changed, so a report is compared for the fields that have been quiet.</summary>
        private void Update()
        {
            BreakdownNet net = BreakdownNet.Instance;
            if (net == null || !net.IsSpawned || !net.IsServer) { return; }
            Clock.Observe("brk.active", net.ActiveMask.ToString(), Time.time);
            Clock.Observe("brk.spot", net.SweetSpotIndex.ToString(), Time.time);
            Clock.Observe("brk.hits", net.HitsDone.ToString(), Time.time);
            Clock.Observe("brk.outlet", net.TrippedOutlet.ToString(), Time.time);
            Clock.Observe("brk.lever", net.LeverUp ? "1" : "0", Time.time);
            Clock.Observe("brk.belt", net.BeltProgress01 > 0f ? "on" : "off", Time.time);
            Clock.Observe("brk.reset", net.ResetProgress01 > 0f ? "on" : "off", Time.time);
        }
    }
}
