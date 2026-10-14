using UnityEngine;

namespace PowerCutCrew.Cables
{
    /// <summary>Scene anchors of the core loop: generator, drum, sockets (with their amp draw) and the starting fuel of each can.</summary>
    public class CoreLoopAnchors : MonoBehaviour
    {
        public static CoreLoopAnchors Instance { get; private set; }

        [SerializeField] private Transform _generator;
        [SerializeField] private Transform _drum;
        [SerializeField] private Transform[] _sockets;
        [SerializeField] private float[] _socketAmps;
        [Tooltip("Fuel in each jerrycan at the start (indexed like the can spawn points).")]
        [SerializeField] private float[] _canStartFuel;

        public Transform Generator => _generator;
        public Transform Drum => _drum;
        public Transform[] Sockets => _sockets;
        public float[] SocketAmps => _socketAmps;
        public float[] CanStartFuel => _canStartFuel;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; }
        }
    }
}
