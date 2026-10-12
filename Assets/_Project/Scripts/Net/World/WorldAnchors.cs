using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>Scene anchors the host spawns the world from: player spawn points, can spawn points, chain position.</summary>
    public class WorldAnchors : MonoBehaviour
    {
        public static WorldAnchors Instance { get; private set; }

        [SerializeField] private Transform[] _playerSpawns;
        [SerializeField] private Transform[] _canSpawns;
        [SerializeField] private Transform _chain;

        public Transform[] CanSpawns => _canSpawns;
        public Vector3 ChainPosition => _chain != null ? _chain.position : Vector3.zero;
        public Transform Chain => _chain;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; }
        }

        public Transform GetPlayerSpawn(int slot)
        {
            return _playerSpawns[Mathf.Clamp(slot, 0, _playerSpawns.Length - 1)];
        }
    }
}
