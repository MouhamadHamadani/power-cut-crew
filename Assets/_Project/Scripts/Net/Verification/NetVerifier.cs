using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>Client side of the -verify assertion: reports the holders and the chain pose it sees to the host, a few times.</summary>
    public class NetVerifier : MonoBehaviour
    {
        private void Start()
        {
            if (LaunchArgs.Verify && !(NetSession.Instance != null && NetSession.Instance.IsHost)) { StartCoroutine(Run()); }
        }

        /// <summary>The game report only (no streams) a moment after a first report, marked "R|": the host re-compares a difference it saw the first time.</summary>
        private static void SendExtras(bool recheck = false)
        {
            var keys = new List<ulong>();
            var seqs = new List<ushort>();
            var counts = new List<int>();
            var flat = new List<Vector3>();
            foreach (SnapshotStream stream in SnapshotStream.Streams)
            {
                if (recheck) { break; }
                if (!stream.HasData || stream.ReceivedPositions == null) { continue; }
                keys.Add(stream.StreamKey);
                seqs.Add(stream.ReceivedSeq);
                counts.Add(stream.ReceivedPositions.Length);
                flat.AddRange(stream.ReceivedPositions);
            }
            string report = (recheck ? NetVerifyExtras.RecheckPrefix : "") + (NetVerifyExtras.BuildReport != null ? NetVerifyExtras.BuildReport() : "");
            NetPlayer.Local.SendVerifyExtras(report, keys.ToArray(), seqs.ToArray(), counts.ToArray(), flat.ToArray());
        }

        private IEnumerator Run()
        {
            while (NetPlayer.Local == null) { yield return null; }
            for (int round = 0; round < 8; round++)
            {
                yield return new WaitForSeconds(round == 0 ? 8f : 10f);
                if (NetPlayer.Local == null) { yield break; }
                var indices = new List<int>();
                var versions = new List<int>();
                var holders = new List<ulong>();
                foreach (NetworkPhysicsItem item in NetworkPhysicsItem.All)
                {
                    if (item.Index < 0) { continue; }
                    indices.Add(item.Index);
                    versions.Add(item.HolderVersion);
                    holders.Add(item.HolderClientId);
                }
                SnapshotStream chain = SnapshotStream.VerifyTarget;
                ushort seq = chain != null ? chain.ReceivedSeq : (ushort)0;
                Vector3[] pos = chain != null && chain.ReceivedPositions != null ? (Vector3[])chain.ReceivedPositions.Clone() : new Vector3[0];
                NetPlayer.Local.SendVerify(indices.ToArray(), versions.ToArray(), holders.ToArray(), seq, pos);
                SendExtras();
                yield return new WaitForSeconds(NetVerifyExtras.RecheckSeconds);
                if (NetPlayer.Local == null) { yield break; }
                SendExtras(true);
            }
        }
    }
}
