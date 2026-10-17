using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// Second part of the late-join assertion (bot runs with -verify): every client reports the newest snapshot it holds
    /// of every snapshot stream (the host checks the positions against what it sent with that sequence number, within
    /// 2 cm) plus a text report built by a game-specific hook (generator, fuel, cables) that the host compares with its
    /// own state in the same hook. Other assemblies plug in through <see cref="BuildReport"/> and <see cref="HostCheck"/>.
    /// </summary>
    public static class NetVerifyExtras
    {
        /// <summary>Client: describe what this machine sees.</summary>
        public static Func<string> BuildReport => () => string.Join(";", Reporters.ConvertAll(r => r()));

        /// <summary>Host: compare a client's report with the host state. Returns null when it matches, "skip" when the host state
        /// was changing too fast to compare, otherwise a description of what differs.</summary>
        public static string HostCheck(string report)
        {
            string all = null;
            foreach (Func<string, string> c in Checkers)
            {
                string d = c(report);
                if (d == "skip") { return d; }
                if (d != null) { all += d; }
            }
            return all;
        }

        private static readonly List<Func<string>> Reporters = new List<Func<string>>();
        private static readonly List<Func<string, string>> Checkers = new List<Func<string, string>>();

        /// <summary>Plug a game system in: its report is joined with the others (";"-separated key=value parts), its check sees the whole report.</summary>
        public static void Add(Func<string> build, Func<string, string> check)
        {
            Reporters.Add(build);
            Checkers.Add(check);
        }

        public const string RecheckPrefix = "R|";
        public const float RecheckSeconds = 0.5f;
        public const float StreamTolerance = 0.02f;

        /// <summary>Host: a game-report difference seen once, waiting for the client's second report.</summary>
        private static readonly Dictionary<ulong, string> Pending = new Dictionary<ulong, string>();
        public const int MaxSnapshotsBehind = 150;

        public static void Check(ulong clientId, string report, ulong[] keys, ushort[] seqs, int[] counts, Vector3[] positions)
        {
            if (report.StartsWith(RecheckPrefix))
            {
                // the same comparison, 0.5 s later, only when the first one differed: a value that was just moving has caught up
                if (!Pending.TryGetValue(clientId, out string first)) { return; }
                Pending.Remove(clientId);
                string again = HostCheck(report.Substring(RecheckPrefix.Length));
                if (again == "skip")
                {
                    NetMetrics.Count("verifyExtraSkipped");
                    Debug.Log($"[Verify2] OK (state changing again, re-check skipped) client {clientId}: first difference was {first.Trim()}");
                }
                else if (again == null)
                {
                    NetMetrics.Count("verifyExtraRecovered");
                    Debug.Log($"[Verify2] OK on re-check client {clientId}: differed at first ({first.Trim()}), the same {RecheckSeconds:F1} s later it matches");
                }
                else
                {
                    NetMetrics.Count("verifyExtraFail");
                    Debug.Log($"[Verify2] FAIL client {clientId}: still differs after {RecheckSeconds:F1} s: {again.Trim()} (first: {first.Trim()})");
                }
                return;
            }
            var sb = new StringBuilder();
            int bad = 0;
            int offset = 0;
            float worst = 0f;
            for (int i = 0; i < keys.Length; i++)
            {
                int n = counts[i];
                SnapshotStream stream = null;
                foreach (SnapshotStream s in SnapshotStream.Streams)
                {
                    if (s.StreamKey == keys[i]) { stream = s; break; }
                }
                if (stream != null && stream.TryGetSentPositions(seqs[i], out Vector3[] sent))
                {
                    float err = 0f;
                    for (int b = 0; b < Mathf.Min(n, sent.Length); b++) { err = Mathf.Max(err, Vector3.Distance(sent[b], positions[offset + b])); }
                    int behind = (ushort)(stream.SentSeq - seqs[i]);
                    worst = Mathf.Max(worst, err);
                    if (err > StreamTolerance || behind > MaxSnapshotsBehind)
                    {
                        bad++;
                        sb.Append($" stream {keys[i]}: error {err * 100f:F1} cm, {behind} snapshots behind;");
                    }
                }
                else if (stream != null)
                {
                    bad++;
                    sb.Append($" stream {keys[i]}: seq {seqs[i]} not in host history;");
                }
                offset += n;
            }

            string diff = HostCheck(report);
            bool differs = diff != null && diff != "skip";
            if (differs) { Pending[clientId] = diff; diff = null; }   // counted only if the re-check still differs
            bool skipped = diff == "skip";
            if (diff != null && !skipped)
            {
                bad++;
                sb.Append(' ').Append(diff);
            }
            string prefix = $"client {clientId}: {keys.Length} streams (worst {worst * 100f:F1} cm)";
            if (bad == 0 && differs) { return; }   // the re-check decides
            if (bad == 0)
            {
                NetMetrics.Count(skipped ? "verifyExtraSkipped" : "verifyExtraOk");
                Debug.Log($"[Verify2] {(skipped ? "OK (state changing, game report skipped)" : "OK")} {prefix} report [{report}]");
            }
            else
            {
                NetMetrics.Count("verifyExtraFail");
                Debug.Log($"[Verify2] FAIL {prefix}:{sb} report [{report}]");
            }
        }
    }
}
