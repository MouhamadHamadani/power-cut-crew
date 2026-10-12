using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// Late-join assertion (bot runs with -verify). The host records every holder change of every item (index, version,
    /// holder) and the snapshots of the chain stream; a client reports what it sees and the host checks the report
    /// against its records: every (item, version) pair must exist with the same holder, the version must be current
    /// (at most one change behind), and the chain positions must equal the host's recorded snapshot with the same
    /// sequence number (within 2 mm) and that snapshot must be recent.
    /// </summary>
    public static class NetVerifyLog
    {
        private static readonly Dictionary<int, Dictionary<int, ulong>> _holders = new Dictionary<int, Dictionary<int, ulong>>();
        private static readonly Dictionary<int, int> _latestVersion = new Dictionary<int, int>();

        public static void Reset()
        {
            _holders.Clear();
            _latestVersion.Clear();
        }

        public static void RecordHolder(int itemIndex, int version, ulong holder)
        {
            if (!LaunchArgs.Verify) { return; }
            if (!_holders.TryGetValue(itemIndex, out Dictionary<int, ulong> versions)) { _holders[itemIndex] = versions = new Dictionary<int, ulong>(); }
            versions[version] = holder;
            _latestVersion[itemIndex] = version;
        }

        /// <summary>Check a client's report. Returns true when everything matches; message describes the first problems.</summary>
        public static bool Check(ulong clientId, int[] itemIndices, int[] versions, ulong[] holders, ushort chainSeq, Vector3[] chainPositions,
            SnapshotStream chain, out string message)
        {
            var sb = new StringBuilder();
            int bad = 0;
            for (int i = 0; i < itemIndices.Length; i++)
            {
                int index = itemIndices[i];
                _holders.TryGetValue(index, out Dictionary<int, ulong> rec);
                int latest = _latestVersion.TryGetValue(index, out int l) ? l : 0;
                bool known = versions[i] == 0 || (rec != null && rec.TryGetValue(versions[i], out ulong h) && h == holders[i]);
                bool current = latest - versions[i] <= 1;
                if (versions[i] == 0 && holders[i] != NetworkPhysicsItem.NoHolder) { known = false; }
                if (!known || !current)
                {
                    bad++;
                    if (bad <= 3) { sb.Append($" item {index}: client v{versions[i]} holder {holders[i]}, host latest v{latest} (known={known} current={current});"); }
                }
            }
            if (chain != null)
            {
                if (chainPositions == null || chainPositions.Length != chain.BodyCount)
                {
                    bad++;
                    sb.Append(" chain: no snapshot received;");
                }
                else if (!chain.TryGetSentPositions(chainSeq, out Vector3[] sent))
                {
                    bad++;
                    sb.Append($" chain: seq {chainSeq} not in host history;");
                }
                else
                {
                    float maxErr = 0f;
                    for (int i = 0; i < sent.Length; i++) { maxErr = Mathf.Max(maxErr, Vector3.Distance(sent[i], chainPositions[i])); }
                    int behind = (ushort)(chain.SentSeq - chainSeq);
                    if (maxErr > 0.002f || behind > 80)
                    {
                        bad++;
                        sb.Append($" chain: max error {maxErr * 1000f:F1} mm, {behind} snapshots behind;");
                    }
                }
            }
            message = $"client {clientId}: {itemIndices.Length} items, chain seq {chainSeq}:{sb}";
            return bad == 0;
        }
    }
}
