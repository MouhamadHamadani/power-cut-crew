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

        private IEnumerator Run()
        {
            while (NetPlayer.Local == null) { yield return null; }
            for (int round = 0; round < 3; round++)
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
                SnapshotStream chain = FindFirstObjectByType<SnapshotStream>();
                ushort seq = chain != null ? chain.ReceivedSeq : (ushort)0;
                Vector3[] pos = chain != null && chain.ReceivedPositions != null ? (Vector3[])chain.ReceivedPositions.Clone() : new Vector3[0];
                NetPlayer.Local.SendVerify(indices.ToArray(), versions.ToArray(), holders.ToArray(), seq, pos);
            }
        }
    }
}
