using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PowerCutCrew.Buildings;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Shift
{
    /// <summary>
    /// Bot-run assertion for the shift (see <see cref="NetVerifyExtras"/>): a client reports shift state, time left, money and, per building,
    /// window / powered / happiness / Angry / current bubble; the host compares with its own. Discrete values only count once the host has
    /// been quiet for a moment, moving values (time, money, happiness) get a tolerance for the sync rate plus the report's trip.
    /// </summary>
    public class ShiftVerify : MonoBehaviour
    {
        private const float QuietSeconds = 2f;
        private const float TimeTolerance = 1.5f;
        private const float HappinessTolerance = 12f;
        private const int MoneyBehind = 12;   // the client's report is older than the host's value; money only rises

        private static float _changedAt;
        private static string _signature = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (!LaunchArgs.Verify) { return; }
            NetVerifyExtras.Add(BuildReport, HostCheck);
            var go = new GameObject("ShiftVerify") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<ShiftVerify>();
        }

        private static string F(float v) => v.ToString("F1", CultureInfo.InvariantCulture);

        private static List<BuildingNet> Sorted()
        {
            var list = new List<BuildingNet>(BuildingNet.All);
            list.RemoveAll(b => b == null || !b.IsSpawned);
            list.Sort((a, b) => a.Index.CompareTo(b.Index));
            return list;
        }

        private static string Bubble(BuildingNet b) => b.HasBubble ? b.BubbleKey : "-";

        private static string BuildReport()
        {
            ShiftNet shift = ShiftNet.Instance;
            var sb = new StringBuilder();
            if (shift != null) { sb.Append($"shift={shift.State},{F(shift.TimeLeft)},{shift.Money}"); }
            sb.Append(";bld=");
            foreach (BuildingNet b in Sorted()) { sb.Append($"{b.Index}:{b.Window}:{(b.Powered ? 1 : 0)}:{F(b.Happiness)}:{(b.Angry ? 1 : 0)}:{Bubble(b)}|"); }
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
            ShiftNet shift = ShiftNet.Instance;
            string[] s = Part(report, "shift").Split(',');
            if (shift != null && s.Length == 3)
            {
                float left = float.Parse(s[1], CultureInfo.InvariantCulture);
                int money = int.Parse(s[2], CultureInfo.InvariantCulture);
                if (s[0] != shift.State.ToString()) { sb.Append($"shift state {s[0]} vs host {shift.State}; "); }
                if (Mathf.Abs(left - shift.TimeLeft) > TimeTolerance) { sb.Append($"time left {left:F1} vs host {shift.TimeLeft:F1}; "); }
                if (money > shift.Money || shift.Money - money > MoneyBehind) { sb.Append($"money {money} vs host {shift.Money}; "); }
            }
            List<BuildingNet> mine = Sorted();
            foreach (string entry in Part(report, "bld").Split('|'))
            {
                string[] f = entry.Split(':');
                if (f.Length != 6) { continue; }
                int index = int.Parse(f[0], CultureInfo.InvariantCulture);
                BuildingNet b = mine.Find(x => x.Index == index);
                if (b == null) { continue; }
                float happy = float.Parse(f[3], CultureInfo.InvariantCulture);
                if (f[1] != b.Window.ToString()) { sb.Append($"building {index} window {f[1]} vs host {b.Window}; "); }
                if (f[2] != (b.Powered ? "1" : "0")) { sb.Append($"building {index} powered {f[2]} vs host {b.Powered}; "); }
                if (Mathf.Abs(happy - b.Happiness) > HappinessTolerance) { sb.Append($"building {index} happiness {happy:F0} vs host {b.Happiness:F0}; "); }
                if (f[4] != (b.Angry ? "1" : "0")) { sb.Append($"building {index} angry {f[4]} vs host {b.Angry}; "); }
                if (f[5] != Bubble(b)) { sb.Append($"building {index} bubble {f[5]} vs host {Bubble(b)}; "); }
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        /// <summary>Host: note when something discrete last changed, so a comparison only happens while the street is quiet.</summary>
        private void Update()
        {
            ShiftNet shift = ShiftNet.Instance;
            if (shift == null || !shift.IsServer) { return; }
            var sb = new StringBuilder();
            sb.Append(shift.State);
            foreach (BuildingNet b in Sorted()) { sb.Append($"|{b.Index}:{b.Window}:{b.Powered}:{b.Angry}:{Bubble(b)}"); }
            string signature = sb.ToString();
            if (signature != _signature)
            {
                _signature = signature;
                _changedAt = Time.time;
            }
        }
    }
}
