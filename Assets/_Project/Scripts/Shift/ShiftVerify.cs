using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PowerCutCrew.Buildings;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Shift
{
    /// <summary>
    /// Bot-run assertion for the shift (see <see cref="NetVerifyExtras"/>): a client reports shift state, time left, money, the summary numbers and, per building,
    /// window / powered / happiness / Angry / current bubble; the host compares with its own, field by field. A discrete value is only compared once the host has
    /// left it alone for a moment (the other fields of the same report still count), moving values (time, money, happiness) get a tolerance for the sync rate plus the report's trip.
    /// </summary>
    public class ShiftVerify : MonoBehaviour
    {
        private const float QuietSeconds = 2f;
        private const float TimeTolerance = 1.5f;
        private const float HappinessTolerance = 12f;
        private const int MoneyBehind = 12;   // the client's report is older than the host's value; money only rises

        private static readonly ChangeClock Clock = new ChangeClock();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (!LaunchArgs.Verify) { return; }
            NetVerifyExtras.AddFields(BuildReport, HostCheck);
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
            if (shift != null)
            {
                sb.Append($"shift={shift.State},{F(shift.TimeLeft)},{shift.Money}");
                ShiftSummaryData s = shift.Summary;
                sb.Append($";sum={s.BreakdownsFixed},{F(s.AvgFixSeconds)},{F(s.UptimePercent)}");
            }
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

        private static bool Changing(string key) => Clock.Changing(key, Time.time, QuietSeconds);

        private static void HostCheck(string report, VerifyFields v)
        {
            ShiftNet shift = ShiftNet.Instance;
            string[] s = Part(report, "shift").Split(',');
            if (shift != null && s.Length == 3)
            {
                float left = float.Parse(s[1], CultureInfo.InvariantCulture);
                int money = int.Parse(s[2], CultureInfo.InvariantCulture);
                v.Field("shift.state", Changing("shift.state"), s[0] == shift.State.ToString(), $"shift state {s[0]} vs host {shift.State}");
                v.Field("shift.time", Changing("shift.state"), Mathf.Abs(left - shift.TimeLeft) <= TimeTolerance, $"time left {left:F1} vs host {shift.TimeLeft:F1}");
                v.Field("shift.money", false, money <= shift.Money && shift.Money - money <= MoneyBehind, $"money {money} vs host {shift.Money}");
            }
            string[] sum = Part(report, "sum").Split(',');
            if (shift != null && sum.Length == 3)
            {
                ShiftSummaryData h = shift.Summary;
                bool changing = Changing("shift.state");   // the summary is written once, in the tick the shift ends
                v.Field("sum.fixed", changing, sum[0] == h.BreakdownsFixed.ToString(CultureInfo.InvariantCulture), $"summary breakdowns fixed {sum[0]} vs host {h.BreakdownsFixed}");
                v.Field("sum.avgFix", changing, sum[1] == F(h.AvgFixSeconds), $"summary average fix time {sum[1]} vs host {F(h.AvgFixSeconds)}");
                v.Field("sum.uptime", changing, sum[2] == F(h.UptimePercent), $"summary uptime {sum[2]} vs host {F(h.UptimePercent)}");
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
                v.Field($"bld{index}.window", Changing($"bld{index}.window"), f[1] == b.Window.ToString(), $"building {index} window {f[1]} vs host {b.Window}");
                v.Field($"bld{index}.powered", Changing($"bld{index}.powered"), f[2] == (b.Powered ? "1" : "0"), $"building {index} powered {f[2]} vs host {b.Powered}");
                v.Field($"bld{index}.happiness", false, Mathf.Abs(happy - b.Happiness) <= HappinessTolerance, $"building {index} happiness {happy:F0} vs host {b.Happiness:F0}");
                v.Field($"bld{index}.angry", Changing($"bld{index}.angry"), f[4] == (b.Angry ? "1" : "0"), $"building {index} angry {f[4]} vs host {b.Angry}");
                v.Field($"bld{index}.bubble", Changing($"bld{index}.bubble"), f[5] == Bubble(b), $"building {index} bubble {f[5]} vs host {Bubble(b)}");
            }
        }

        /// <summary>Host: note when each discrete value last changed, so a report is compared for the fields that have been quiet.</summary>
        private void Update()
        {
            ShiftNet shift = ShiftNet.Instance;
            if (shift == null || !shift.IsServer) { return; }
            Clock.Observe("shift.state", shift.State.ToString(), Time.time);
            foreach (BuildingNet b in Sorted())
            {
                Clock.Observe($"bld{b.Index}.window", b.Window.ToString(), Time.time);
                Clock.Observe($"bld{b.Index}.powered", b.Powered.ToString(), Time.time);
                Clock.Observe($"bld{b.Index}.angry", b.Angry.ToString(), Time.time);
                Clock.Observe($"bld{b.Index}.bubble", Bubble(b), Time.time);
            }
        }
    }
}
