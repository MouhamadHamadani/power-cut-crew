using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Generator
{
    /// <summary>
    /// Thin network wrapper of the <see cref="GeneratorModel"/>: the host runs the model every physics step and
    /// publishes the state at once and fuel / heat / load as quantised values at a low rate, only when they changed.
    /// Clients just read the NetworkVariables (panel, prompts).
    /// </summary>
    public class GeneratorNet : NetworkBehaviour
    {
        public static GeneratorNet Instance { get; private set; }

        [SerializeField] private Transform[] _outlets;
        [SerializeField] private Transform _cordPoint;
        [SerializeField] private ParticleSystem _sputter;

        private readonly NetworkVariable<byte> _state = new NetworkVariable<byte>((byte)GeneratorState.Off);
        private readonly NetworkVariable<float> _fuel = new NetworkVariable<float>();
        private readonly NetworkVariable<float> _heat = new NetworkVariable<float>();
        private readonly NetworkVariable<float> _load = new NetworkVariable<float>();

        private GeneratorModel _model;
        private System.Random _rng;
        private float _nextSync;
        private float _shake;

        /// <summary>Host: which cable feeds which socket.</summary>
        public PowerGrid Grid { get; } = new PowerGrid();

        public Transform[] Outlets => _outlets;
        public Transform CordPoint => _cordPoint;

        public GeneratorState State => IsServer && _model != null ? _model.State : (GeneratorState)_state.Value;
        public float Fuel => IsServer && _model != null ? _model.Fuel : _fuel.Value;
        public float Heat => IsServer && _model != null ? _model.Heat : _heat.Value;
        public float Load => IsServer && _model != null ? _model.LoadAmps : _load.Value;
        public float Capacity => GeneratorTuning.Instance.Capacity;
        public float RatedAmps => GeneratorTuning.Instance.RatedAmps;
        public bool IsRunning => State == GeneratorState.Running;
        public GeneratorModel Model => _model;

        private void Awake()
        {
            if (Instance == null) { Instance = this; }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) { Instance = null; }
        }

        public override void OnNetworkSpawn()
        {
            // the host's instance wins if two network managers live in one process (in-process tests)
            if (IsServer || Instance == null || !Instance.IsServer) { Instance = this; }
            if (!IsServer) { return; }
            GeneratorTuning t = GeneratorTuning.Instance;
            _model = new GeneratorModel(t.ToSettings(), t.StartFuel);
            _rng = new System.Random(LaunchArgs.Seed * 7919 + 13);
            Publish(true);
        }

        /// <summary>Host: back to the scene defaults (off, start fuel, cold, nothing plugged in) for the next shift.</summary>
        public void HostReset()
        {
            GeneratorTuning t = GeneratorTuning.Instance;
            _model = new GeneratorModel(t.ToSettings(), t.StartFuel);
            Grid.DisconnectAll();
            Publish(true);
        }

        public Vector3 OutletPosition(int outlet) => _outlets[outlet].position;

        // ---------- host actions ----------

        public PullResult HostPull(int playerId)
        {
            PullResult r = _model.Pull(_rng.NextDouble());
            switch (r)
            {
                case PullResult.Started:
                    NetMetrics.Count("pullsOk");
                    Debug.Log($"[Gen] started by player {playerId}");
                    break;
                case PullResult.Failed:
                case PullResult.NoFuel:
                case PullResult.Locked:
                    NetMetrics.Count("pullsFail");
                    SputterRpc((byte)r);
                    break;
            }
            Publish(true);
            return r;
        }

        public bool HostStop()
        {
            bool stopped = _model.Stop();
            if (stopped) { Debug.Log("[Gen] turned off"); Publish(true); }
            return stopped;
        }

        public float HostAddFuel(float amount)
        {
            float accepted = _model.AddFuel(amount);
            if (accepted > 0f) { Publish(false); }
            return accepted;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _model == null) { return; }
            GeneratorState before = _model.State;
            _model.Tick(Time.fixedDeltaTime, Grid.Load);
            if (_model.State != before)
            {
                if (_model.State == GeneratorState.Overheated) { NetMetrics.Count("overheats"); Debug.Log("[Gen] overheated"); }
                if (_model.State == GeneratorState.OutOfFuel) { NetMetrics.Count("outOfFuel"); Debug.Log("[Gen] out of fuel"); }
                Publish(true);
            }
            else
            {
                Publish(false);
            }
        }

        /// <summary>State goes out at once when it changed; the numbers at most 4 Hz and only when their quantised value changed.</summary>
        private void Publish(bool force)
        {
            byte state = (byte)_model.State;
            if (_state.Value != state) { _state.Value = state; }
            if (!force && Time.unscaledTime < _nextSync) { return; }
            _nextSync = Time.unscaledTime + 1f / Mathf.Max(0.1f, GeneratorTuning.Instance.SyncRateHz);
            float fuel = Mathf.Round(_model.Fuel * 2f) / 2f;
            float heat = Mathf.Round(_model.Heat);
            float load = Mathf.Round(_model.LoadAmps * 10f) / 10f;
            if (!Mathf.Approximately(fuel, _fuel.Value)) { _fuel.Value = fuel; }
            if (!Mathf.Approximately(heat, _heat.Value)) { _heat.Value = heat; }
            if (!Mathf.Approximately(load, _load.Value)) { _load.Value = load; }
        }

        // ---------- everybody: sputter ----------

        [Rpc(SendTo.Everyone)]
        private void SputterRpc(byte result)
        {
            Debug.Log($"[Gen] sputter ({(PullResult)result})");
            if (_sputter != null) { _sputter.Play(); }
            _shake = 0.35f;
        }

        private void Update()
        {
            if (_shake > 0f)
            {
                _shake -= Time.deltaTime;
                float k = Mathf.Max(0f, _shake) / 0.35f;
                transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 60f) * 3f * k);
            }
            else if (transform.localRotation != Quaternion.identity)
            {
                transform.localRotation = Quaternion.identity;
            }
        }
    }
}
