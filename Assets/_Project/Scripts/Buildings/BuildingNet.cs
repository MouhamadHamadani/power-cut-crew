using System.Collections.Generic;
using PowerCutCrew.Cables;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Buildings
{
    /// <summary>What a neighbor shouts about.</summary>
    public enum BubbleTrigger : byte { Request = 0, Unpowered = 1, Spike = 2, Angry = 3 }

    /// <summary>How the marker above a building looks.</summary>
    public enum MarkerState : byte { Quiet = 0, Powered = 1, Unpowered = 2, Angry = 3 }

    /// <summary>
    /// A building: the host runs its <see cref="BuildingModel"/> (the <see cref="BuildingDirector"/> ticks it), the state goes out as NetworkVariables
    /// (changes at once, happiness quantised and at most 4 Hz), and every machine draws the marker, the window lights and the speech bubble from them.
    /// Building i owns socket i.
    /// </summary>
    public class BuildingNet : NetworkBehaviour
    {
        public static readonly List<BuildingNet> All = new List<BuildingNet>();
        public const int LinesPerTrigger = 4;

        [SerializeField] private BuildingMarker _marker;
        [SerializeField] private BuildingBubble _bubble;
        [SerializeField] private Renderer[] _windows;
        [SerializeField] private Material _windowLit;
        [SerializeField] private Material _windowDark;
        [Header("Layout (the height comes from the tuning entry)")]
        [SerializeField] private Transform _body;
        [SerializeField] private Transform _markerRoot;
        [SerializeField] private Transform _bubbleRoot;
        [SerializeField] private int _windowColumns = 3;
        [SerializeField] private float _windowRowSpacing = 1.5f;

        private readonly NetworkVariable<int> _index = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<byte> _window = new NetworkVariable<byte>((byte)DemandWindow.Quiet);
        private readonly NetworkVariable<bool> _spiking = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<bool> _angry = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<byte> _happiness = new NetworkVariable<byte>(70);
        private readonly NetworkVariable<int> _bubbleId = new NetworkVariable<int>(0);
        private readonly NetworkVariable<byte> _bubbleTrigger = new NetworkVariable<byte>(0);
        private readonly NetworkVariable<byte> _bubbleLine = new NetworkVariable<byte>(0);
        private readonly NetworkVariable<float> _bubbleStart = new NetworkVariable<float>(-1000f);

        private float _nextHappinessSync;
        private bool _lit;
        private bool _litKnown;

        public int Index => _index.Value;
        public DemandWindow Window => (DemandWindow)_window.Value;
        public bool Spiking => _spiking.Value;
        public bool Angry => _angry.Value;
        public float Happiness => IsServer && Model != null ? Model.Happiness : _happiness.Value;
        public float SubscriptionAmps => Entry.SubscriptionAmps;
        public bool Requesting => Window == DemandWindow.Requesting;

        /// <summary>Host: the rules.</summary>
        public BuildingModel Model { get; private set; }

        public BuildingEntry Entry
        {
            get
            {
                BuildingEntry[] all = BuildingTuning.Instance.Buildings;
                int i = Mathf.Clamp(Index, 0, all.Length - 1);
                return all[i];
            }
        }

        public PowerSocket Socket => PowerSocket.ById(Index);   // a handful of sockets: no cache (a cached copy can outlive a respawn, or be a client copy when the host's exists)
        public bool Powered => Socket != null && Socket.Powered;

        public MarkerState Marker
        {
            get
            {
                if (Angry) { return MarkerState.Angry; }
                if (!Requesting) { return MarkerState.Quiet; }
                return Powered ? MarkerState.Powered : MarkerState.Unpowered;
            }
        }

        /// <summary>Seconds of the current speech bubble that are left (0 when there is none).</summary>
        public float BubbleSecondsLeft
        {
            get
            {
                if (_bubbleId.Value == 0 || NetworkManager == null) { return 0f; }
                float shown = (float)NetworkManager.ServerTime.Time - _bubbleStart.Value;
                return Mathf.Max(0f, BuildingTuning.Instance.BubbleSeconds - shown);
            }
        }

        public string BubbleKey => $"bld.line.{BubbleName((BubbleTrigger)_bubbleTrigger.Value)}.{_bubbleLine.Value}";

        public static string BubbleName(BubbleTrigger t)
        {
            switch (t)
            {
                case BubbleTrigger.Request: return "request";
                case BubbleTrigger.Unpowered: return "unpowered";
                case BubbleTrigger.Spike: return "spike";
                default: return "angry";
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!All.Contains(this)) { All.Add(this); }
            _litKnown = false;
            ApplyLayout();
            _index.OnValueChanged += (previous, now) => ApplyLayout();
        }

        /// <summary>A mixed skyline: the body is as tall as its tuning entry says, only the window rows that fit are there, the marker and bubble sit above the roof.</summary>
        private void ApplyLayout()
        {
            if (Index < 0 || _body == null) { return; }
            float height = Mathf.Max(2f, Entry.HeightMeters);
            _body.localScale = new Vector3(4f, height, 4f);
            _body.localPosition = new Vector3(0f, height * 0.5f, 0f);
            int rows = Mathf.Max(1, Mathf.FloorToInt((height - 1f) / _windowRowSpacing));
            for (int i = 0; i < _windows.Length; i++)
            {
                if (_windows[i] == null) { continue; }
                int row = i / _windowColumns;
                _windows[i].gameObject.SetActive(row < rows);
                if (row < rows)
                {
                    Vector3 p = _windows[i].transform.localPosition;
                    _windows[i].transform.localPosition = new Vector3(p.x, 0.8f + row * _windowRowSpacing, p.z);
                }
            }
            if (_markerRoot != null) { _markerRoot.localPosition = new Vector3(0f, height + 1.6f, 0f); }
            if (_bubbleRoot != null) { _bubbleRoot.localPosition = new Vector3(0f, height + 3.4f, 0f); }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
        }

        // ---------- host ----------

        /// <summary>Host: call right after Spawn(). The building owns the socket with the same index.</summary>
        public void ServerInit(int index)
        {
            BuildingTuning t = BuildingTuning.Instance;
            _index.Value = index;
            ApplyLayout();
            BuildingEntry e = t.Buildings[index];
            Model = new BuildingModel(t.ToSettings(), e.SubscriptionAmps, LaunchArgs.Seed * 1009 + e.SeedOffset);
            GeneratorNet gen = GeneratorNet.Instance;
            if (gen != null) { gen.Grid.SetDraw(index, 0f); }   // nothing is drawn until the shift runs
            Debug.Log($"[Building] {index} ready: {e.NameKey}, {e.SubscriptionAmps} A");
        }

        /// <summary>Host: copy the rules' state into the NetworkVariables (state at once, happiness at most 4 Hz and only when its value changed).</summary>
        public void HostPublish()
        {
            if (Model == null) { return; }
            byte window = (byte)Model.Window;
            if (_window.Value != window) { _window.Value = window; }
            if (_spiking.Value != Model.Spiking) { _spiking.Value = Model.Spiking; }
            if (_angry.Value != Model.Angry) { _angry.Value = Model.Angry; }
            if (Time.unscaledTime >= _nextHappinessSync)
            {
                _nextHappinessSync = Time.unscaledTime + 1f / Mathf.Max(0.1f, BuildingTuning.Instance.SyncRateHz);
                byte h = (byte)Mathf.Clamp(Mathf.RoundToInt(Model.Happiness), 0, 100);
                if (_happiness.Value != h) { _happiness.Value = h; }
            }
        }

        public void HostPublishNow()
        {
            _nextHappinessSync = 0f;
            HostPublish();
        }

        /// <summary>Host: a neighbor shouts one of its lines.</summary>
        public void HostShowBubble(BubbleTrigger trigger, System.Random rng)
        {
            _bubbleTrigger.Value = (byte)trigger;
            _bubbleLine.Value = (byte)rng.Next(LinesPerTrigger);
            _bubbleStart.Value = (float)NetworkManager.ServerTime.Time;
            _bubbleId.Value++;
            NetMetrics.Count("bubbles");
        }

        public void HostClearBubble()
        {
            if (_bubbleId.Value != 0) { _bubbleStart.Value = -1000f; _bubbleId.Value = 0; }
        }

        // ---------- everybody: what it looks like ----------

        private void Update()
        {
            if (!IsSpawned) { return; }
            bool powered = Powered;
            if (!_litKnown || powered != _lit)
            {
                _lit = powered;
                _litKnown = true;
                Material m = powered ? _windowLit : _windowDark;
                foreach (Renderer r in _windows)
                {
                    if (r != null) { r.sharedMaterial = m; }
                }
            }
            if (_marker != null) { _marker.Show(Marker, SubscriptionAmps * (Spiking ? BuildingTuning.Instance.SpikeMultiplier : 1f), Spiking, Entry.NameKey); }
            if (_bubble != null)
            {
                float left = BubbleSecondsLeft;
                if (left > 0f) { _bubble.Show(BubbleKey); } else { _bubble.Hide(); }
            }
        }
    }
}
