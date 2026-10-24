using System;
using System.Collections.Generic;
using PowerCutCrew.Core;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>What a sound hook plays (no sounds are in the project yet: <see cref="BreakdownNet.Sound"/> is where they plug in).</summary>
    public enum BreakdownSound : byte { Cough, Clang, Hit, Fixed, Squeal, BeltFit, Bang, Spark, LeverUp, LeverDown }

    /// <summary>
    /// The three breakdowns on the network (on the generator). The host runs the <see cref="BreakdownModel"/> and the <see cref="BreakdownDirector"/> and writes everything
    /// the others need into NetworkVariables: the active kinds, the sweet spot and the hits done, the tripped outlet, the lever and the fix progress. Smoke, sparks, shaking,
    /// the glowing spots and the lever are all drawn locally from those (<see cref="BreakdownVisuals"/>), so late joiners see the current state and nothing is sent per frame.
    /// Clients only send requests (through the wrench, the belt slot and the lever); the host validates and answers.
    /// </summary>
    [RequireComponent(typeof(GeneratorNet))]
    public class BreakdownNet : NetworkBehaviour
    {
        public static BreakdownNet Instance { get; private set; }

        /// <summary>Every machine: something should make a sound (the hook for audio, later).</summary>
        public static event Action<BreakdownSound, Vector3> Sound;

        /// <summary>Host: a breakdown just started / was fixed (the shift shows the neighbours' lines, counts them).</summary>
        public static event Action<BreakdownType> Started;
        public static event Action<BreakdownType, float> Fixed;

        private const int SlotLockId = 1, LeverLockId = 2;

        [SerializeField] private Transform[] _sweetSpots;
        [SerializeField] private Transform _beltSlot;
        [SerializeField] private Transform _lever;
        [SerializeField] private Transform _crateSpawn;
        [SerializeField] private Transform _wrenchHook;

        private readonly NetworkVariable<byte> _active = new NetworkVariable<byte>(0);
        private readonly NetworkVariable<byte> _sweetSpot = new NetworkVariable<byte>(0);
        private readonly NetworkVariable<byte> _hits = new NetworkVariable<byte>(0);
        private readonly NetworkVariable<int> _tripped = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<bool> _leverUp = new NetworkVariable<bool>(true);
        private readonly NetworkVariable<byte> _beltProgress = new NetworkVariable<byte>(0);
        private readonly NetworkVariable<byte> _resetProgress = new NetworkVariable<byte>(0);

        // host only
        private BreakdownModel _model;
        private BreakdownDirector _director;
        private bool _shiftRunning;
        private float _shiftClock;
        private float _nextWrenchLog;
        private bool _randomOff;
        private BreakdownType? _forcedType;
        private float _forcedAt;
        private float _forcedClock;
        private float _forcedDue = -1f;
        private readonly InteractionArbiter _arbiter = new InteractionArbiter();
        private readonly Dictionary<int, float> _lastSwing = new Dictionary<int, float>();
        private ItemRespawnRule _wrenchRule;

        public IReadOnlyList<Transform> SweetSpots => _sweetSpots;
        public Transform BeltSlot => _beltSlot;
        public Transform Lever => _lever;
        public Transform CrateSpawn => _crateSpawn;
        public Transform WrenchHook => _wrenchHook;
        public InteractionArbiter Arbiter => _arbiter;

        public byte ActiveMask => _active.Value;
        public bool IsActive(BreakdownType t) => BreakdownTypes.Has(_active.Value, t);
        public int ActiveCount => BreakdownTypes.CountOf(_active.Value);
        public int SweetSpotIndex => _sweetSpot.Value;
        public int HitsDone => _hits.Value;
        public int TrippedOutlet => _tripped.Value;

        /// <summary>The lever is in its normal (up) position.</summary>
        public bool LeverUp => !IsActive(BreakdownType.Breaker) || _leverUp.Value;

        public float BeltProgress01 => _beltProgress.Value / 100f;
        public float ResetProgress01 => _resetProgress.Value / 100f;

        /// <summary>Host: the numbers for the summary.</summary>
        public int FixedCount => _model != null ? _model.FixedCount : 0;
        public float AverageFixSeconds => _model != null ? _model.AverageFixSeconds : 0f;
        public int Retrips => _model != null ? _model.RetripCount : 0;
        public int StartedCount => _director != null ? _director.Started : 0;
        public BreakdownModel Model => _model;

        public static BreakdownSettings Settings => BreakdownTuning.Instance.ToSettings();

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
            BreakdownTuning t = BreakdownTuning.Instance;
            _model = new BreakdownModel(t.ToSettings());
            _director = new BreakdownDirector(t.ToSettings(), LaunchArgs.Seed * 53 + 17);
            _wrenchRule = new ItemRespawnRule(t.ItemRespawnSeconds, t.LostDistanceMeters, t.FallBelowY);
            _randomOff = LaunchArgs.Get("-breakdowns") == "off";
            string forced = LaunchArgs.Get("-breakdown");
            if (!string.IsNullOrEmpty(forced))
            {
                _randomOff = true;   // a forced breakdown is the only one
                _forcedType = ParseType(forced);
                _forcedAt = Mathf.Max(0.5f, LaunchArgs.GetFloat("-breakdownat", 5f));
            }
            Publish();
            StartCoroutine(BreakdownWorld.SpawnWrenchSoon(this));
        }

        public static BreakdownType? ParseType(string name)
        {
            for (int i = 0; i < BreakdownTypes.Count; i++)
            {
                if (string.Equals(BreakdownTypes.Name((BreakdownType)i), name, StringComparison.OrdinalIgnoreCase)) { return (BreakdownType)i; }
            }
            return null;
        }

        // ---------- host: the shift ----------

        /// <summary>Host: the power cut begins. Fresh dice, no breakdowns, the grace period starts.</summary>
        public void HostBeginShift()
        {
            if (!IsServer) { return; }
            _director.Reset();
            _model.ClearAll();
            _model.ResetStats();
            _lastSwing.Clear();
            _arbiter.Clear();
            _shiftRunning = true;
            _shiftClock = 0f;
            _forcedClock = 0f;
            _forcedDue = _forcedType.HasValue ? _forcedAt : -1f;
            Publish();
            Debug.Log($"[Breakdown] shift begins ({(_randomOff ? "random breakdowns off" + (_forcedType.HasValue ? ", forcing " + _forcedType.Value + " at " + _forcedAt.ToString("F0") + " s" : "") : "random breakdowns on")})");
        }

        /// <summary>Host: the shift is over. Whatever is still broken is forgiven (the numbers stay for the summary).</summary>
        public void HostEndShift()
        {
            if (!IsServer) { return; }
            _shiftRunning = false;
            Debug.Log($"[Breakdown] shift over: {_director.Started} started, {_model.FixedCount} fixed, average fix {_model.AverageFixSeconds:F1} s, {_model.RetripCount} re-trips, {_model.ActiveCount} still active");
            ClearActive();
        }

        /// <summary>Host: back to the scene defaults (the street between shifts): nothing broken, no loose belts, the wrench on its hook.</summary>
        public void HostReset()
        {
            if (!IsServer) { return; }
            _shiftRunning = false;
            ClearActive();
            _model.ResetStats();
            _arbiter.Clear();
            BreakdownWorld.ResetItems();
            _wrenchRule.Reset();
        }

        private void ClearActive()
        {
            _model.ClearAll();
            _forcedDue = -1f;
            Apply();
            Publish();
        }

        // ---------- host: every physics step ----------

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _model == null) { return; }
            GeneratorNet gen = GeneratorNet.Instance;
            if (gen == null || gen.Model == null) { return; }
            float dt = Time.fixedDeltaTime;

            if (_shiftRunning)
            {
                _shiftClock += dt;
                var inputs = new DirectorInputs
                {
                    ShiftRunning = true,
                    GeneratorRunning = gen.IsRunning,
                    FuelFraction = gen.Capacity > 0f ? gen.Fuel / gen.Capacity : 1f,
                    Heat = gen.Heat,
                    LoadAmps = gen.Load,
                    RatedAmps = gen.RatedAmps,
                    ActiveMask = _model.ActiveMask,
                };
                BreakdownType? started = _randomOff ? null : _director.Tick(dt, inputs);
                if (started.HasValue) { HostStart(started.Value); }
                TickForced(dt);
            }

            bool retripped = _model.Tick(dt);
            if (retripped)
            {
                Debug.Log("[Breakdown] the breaker fell back: the street was still overloaded");
                BreakerFellRpc();
            }
            Apply();
            Publish();
            TickWrench(dt);
        }

        /// <summary>-breakdown: the forced type starts a set time into the shift, and again that long after each fix.</summary>
        private void TickForced(float dt)
        {
            if (!_forcedType.HasValue || _forcedDue < 0f) { return; }
            _forcedClock += dt;
            if (_forcedClock < _forcedDue) { return; }
            if (_model.IsActive(_forcedType.Value)) { _forcedClock = _forcedDue - 0.5f; return; }
            _forcedDue = -1f;
            HostStart(_forcedType.Value);
        }

        private void HostStart(BreakdownType type)
        {
            GeneratorNet gen = GeneratorNet.Instance;
            int spot = _director.PickSweetSpot();
            int outlet = _director.PickOutlet(gen.Grid.ConnectedOutlets(), gen.Outlets.Length);
            if (!_model.Start(type, spot, outlet)) { return; }
            if (type == BreakdownType.Breaker) { _leverUp.Value = false; }
            NetMetrics.Count("breakdowns");
            NetMetrics.Count("breakdown_" + BreakdownTypes.Name(type));
            Debug.Log($"[Breakdown] {type} starts at {_shiftClock:F0} s (sweet spot {spot}, outlet {outlet}, load {gen.Load:F1} A, heat {gen.Heat:F0}, fuel {gen.Fuel:F0})");
            Apply();
            Publish();
            StartedRpc((byte)type);
            Started?.Invoke(type);
        }

        /// <summary>Host: the model's state goes into the generator and the grid.</summary>
        private void Apply()
        {
            GeneratorNet gen = GeneratorNet.Instance;
            if (gen == null || gen.Model == null) { return; }
            BreakdownTuning t = BreakdownTuning.Instance;
            gen.Model.OutputDip = _model.Dipping;
            bool belt = _model.IsActive(BreakdownType.Belt);
            gen.Model.CoolingBroken = belt;
            gen.Model.BrokenCoolingExtraHeat = belt ? t.BeltExtraHeatPerSecond : 0f;
            gen.Grid.ResetTrips();
            if (_model.OutletTripped && _model.TrippedOutlet >= 0) { gen.Grid.SetTripped(_model.TrippedOutlet, true); }
        }

        private void Publish()
        {
            BreakdownTuning t = BreakdownTuning.Instance;
            if (_active.Value != _model.ActiveMask) { _active.Value = _model.ActiveMask; }
            byte spot = (byte)_model.SweetSpot;
            if (_sweetSpot.Value != spot) { _sweetSpot.Value = spot; }
            byte hits = (byte)_model.HitsDone;
            if (_hits.Value != hits) { _hits.Value = hits; }
            if (_tripped.Value != _model.TrippedOutlet) { _tripped.Value = _model.TrippedOutlet; }
            bool up = _model.LeverUp || !_model.IsActive(BreakdownType.Breaker);
            if (_leverUp.Value != up) { _leverUp.Value = up; }
            byte belt = Step(_model.BeltProgress, t.ProgressStepPercent);
            if (_beltProgress.Value != belt) { _beltProgress.Value = belt; }
            byte reset = Step(_model.ResetProgress, t.ProgressStepPercent);
            if (_resetProgress.Value != reset) { _resetProgress.Value = reset; }
        }

        private static byte Step(float fraction01, int stepPercent)
        {
            int step = Mathf.Max(1, stepPercent);
            int percent = Mathf.Clamp(Mathf.RoundToInt(fraction01 * 100f), 0, 100);
            return (byte)(percent / step * step);
        }

        private void TickWrench(float dt)
        {
            WrenchItem wrench = WrenchItem.Instance;
            if (wrench == null || !wrench.Item.IsServer || _wrenchRule == null) { return; }
            NetworkPhysicsItem item = wrench.Item;
            if (LaunchArgs.Has("-wrenchlog") && Time.time > _nextWrenchLog)
            {
                _nextWrenchLog = Time.time + 1f;
                Debug.Log($"[Wrench] hook {WrenchHome(item)} at {item.transform.position} v {item.Body.linearVelocity.magnitude:F1} kinematic {item.Body.isKinematic} held {item.IsHeld}");
            }
            if (_wrenchRule.Tick(dt, item.IsHeld, item.transform.position, WrenchHome(item)))
            {
                Debug.Log("[Breakdown] the lost wrench goes back to its hook");
                NetMetrics.Count("wrenchRespawns");
                item.HostResetToHome();
            }
        }

        private Vector3 WrenchHome(NetworkPhysicsItem item) => _wrenchHook != null ? _wrenchHook.position : item.transform.position;

        // ---------- host: requests from the items ----------

        /// <summary>Host: the wrench swung. Validates the holder's reach and rhythm, then asks the model whether the hit landed on the sweet spot.</summary>
        public void HostWrenchHit(int playerId, Component target, Vector3 point)
        {
            if (!IsServer || _model == null) { return; }
            BreakdownTuning t = BreakdownTuning.Instance;
            if (!NetPlayer.ByClient.TryGetValue((ulong)playerId, out NetPlayer player)) { return; }
            // rhythm: one swing per cooldown (a little slack for the network)
            float now = Time.unscaledTime;
            if (_lastSwing.TryGetValue(playerId, out float last) && now - last < t.SwingCooldown * 0.8f) { NetMetrics.Count("wrenchTooFast"); return; }
            _lastSwing[playerId] = now;
            // reach: from the player's eye to the point, with a little slack for the age of the holder's view
            Vector3 eye = player.transform.position + Vector3.up * PlayerTuning.Instance.EyeHeightStanding;
            float reach = Vector3.Distance(eye, point);
            if (reach > t.HitRange + t.HitRangeSlack) { NetMetrics.Count("wrenchOutOfRange"); return; }
            // target: it has to be this generator (the holder's ray hit it)
            if (target == null || target.GetComponentInParent<BreakdownNet>() != this) { NetMetrics.Count("wrenchWrongTarget"); return; }

            int spot = SweetSpotRule.Nearest(point, SweetSpotPositions(), t.SweetSpotRadius);
            HitResult result = _model.Hit(spot);
            NetMetrics.Count("wrenchHits");
            NetMetrics.Count("wrenchHit_" + result);
            Debug.Log($"[Breakdown] wrench hit by player {playerId}: {result} (spot {spot}, sweet spot {_model.SweetSpot}, hits {_model.HitsDone}, reach {reach:F2} m)");
            HitFeedbackRpc((byte)result, point);
            if (result == HitResult.Fixed) { OnFixed(BreakdownType.Sputter); }
            Apply();
            Publish();
        }

        private List<Vector3> _spotPositions = new List<Vector3>();

        private List<Vector3> SweetSpotPositions()
        {
            _spotPositions.Clear();
            foreach (Transform s in _sweetSpots) { _spotPositions.Add(s.position); }
            return _spotPositions;
        }

        /// <summary>Host: may this player hold the belt slot / lever (nobody else does)?</summary>
        public bool HostCanHold(int lockId, int playerId) => !_arbiter.IsHeld((ulong)lockId) || _arbiter.HolderOf((ulong)lockId) == playerId;

        public bool HostBeginBeltFit(int playerId) => _arbiter.TryAcquire(SlotLockId, playerId) || _arbiter.HolderOf(SlotLockId) == playerId;
        public bool HostBeginReset(int playerId) => _arbiter.TryAcquire(LeverLockId, playerId) || _arbiter.HolderOf(LeverLockId) == playerId;
        public const int SlotId = SlotLockId;
        public const int LeverId = LeverLockId;

        public void HostEndHold(int lockId, int playerId)
        {
            _arbiter.Release((ulong)lockId, playerId);
            if (lockId == SlotLockId) { _model.CancelFit(); }
            else { _model.CancelReset(); }
            Publish();
        }

        /// <summary>Host: the fit button is held with a belt at the slot. True while it goes on; false once the belt is on.</summary>
        public bool HostFitBelt(float dt, out bool done)
        {
            done = false;
            if (!_model.IsActive(BreakdownType.Belt)) { return false; }
            if (_model.FitBelt(dt))
            {
                done = true;
                Debug.Log("[Breakdown] the new belt is on");
                OnFixed(BreakdownType.Belt);
                BeltFittedRpc(UnityEngine.Random.Range(0, int.MaxValue));
                Apply();
                Publish();
                return false;
            }
            Publish();
            return true;
        }

        /// <summary>Host: the lever is held. False once the reset went through (fixed, or it will fall back).</summary>
        public bool HostResetBreaker(float dt)
        {
            GeneratorNet gen = GeneratorNet.Instance;
            if (!_model.IsActive(BreakdownType.Breaker) || gen == null) { return false; }
            float restored = gen.Grid.LoadIfNothingTripped;
            ResetResult r = _model.ResetBreaker(dt, restored, gen.RatedAmps);
            switch (r)
            {
                case ResetResult.Fixed:
                    Debug.Log($"[Breakdown] breaker reset (load {restored:F1} A of {gen.RatedAmps:F0})");
                    OnFixed(BreakdownType.Breaker);
                    LeverRpc(true);
                    break;
                case ResetResult.WillRetrip:
                    Debug.Log($"[Breakdown] breaker reset while overloaded ({restored:F1} A of {gen.RatedAmps:F0}): it will fall back");
                    NetMetrics.Count("breakerRetrips");
                    LeverRpc(true);
                    break;
            }
            Apply();
            Publish();
            return r == ResetResult.Holding;
        }

        private void OnFixed(BreakdownType type)
        {
            NetMetrics.Count("breakdownsFixed");
            NetMetrics.Sample("breakdownFixSeconds", _model.LastFixSeconds);
            Fixed?.Invoke(type, _model.LastFixSeconds);
            FixedRpc((byte)type);
            if (_forcedType == type) { _forcedClock = 0f; _forcedDue = _forcedAt; }   // -breakdown: the next one comes a set time after this fix
        }

        // ---------- host: development and test hooks (doc screenshots, net tests) ----------

        /// <summary>Host: start a breakdown right now, with a chosen sweet spot / outlet (-1: let the dice pick).</summary>
        public bool HostForce(BreakdownType type, int sweetSpot = -1, int outlet = -1)
        {
            if (!IsServer || _model == null || _model.IsActive(type)) { return false; }
            GeneratorNet gen = GeneratorNet.Instance;
            int spot = sweetSpot >= 0 ? sweetSpot : _director.PickSweetSpot();
            int pick = outlet >= 0 ? outlet : _director.PickOutlet(gen.Grid.ConnectedOutlets(), gen.Outlets.Length);
            if (!_model.Start(type, spot, pick)) { return false; }
            if (type == BreakdownType.Breaker) { _leverUp.Value = false; }
            NetMetrics.Count("breakdowns");
            NetMetrics.Count("breakdown_" + BreakdownTypes.Name(type));
            Debug.Log($"[Breakdown] {type} forced (sweet spot {spot}, outlet {pick})");
            Apply();
            Publish();
            StartedRpc((byte)type);
            Started?.Invoke(type);
            return true;
        }

        /// <summary>Host: the wrench lands on the sweet spot (or a hand's width beside it), as if a player had swung it.</summary>
        public HitResult HostDevHit(bool onSweetSpot)
        {
            Transform spot = _sweetSpots[Mathf.Clamp(_model.SweetSpot, 0, _sweetSpots.Length - 1)];
            Vector3 point = spot.position + (onSweetSpot ? Vector3.zero : Vector3.up * 0.5f);
            HitResult result = _model.Hit(onSweetSpot ? _model.SweetSpot : -1);
            HitFeedbackRpc((byte)result, point);
            if (result == HitResult.Fixed) { OnFixed(BreakdownType.Sputter); }
            Apply();
            Publish();
            return result;
        }

        /// <summary>Host: hold the belt fit / the breaker reset for part of the time (a bar fills), without finishing it.</summary>
        public void HostDevProgress(BreakdownType type, float seconds)
        {
            if (type == BreakdownType.Belt) { _model.FitBelt(seconds); }
            else if (type == BreakdownType.Breaker) { _model.ResetBreaker(seconds, 0f, GeneratorNet.Instance.RatedAmps); }
            Publish();
        }

        /// <summary>Host: finish a breakdown the way a player would (counts as a fix).</summary>
        public void HostDevFix(BreakdownType type)
        {
            switch (type)
            {
                case BreakdownType.Sputter:
                    for (int i = 0; i < 10 && _model.IsActive(type); i++) { HostDevHit(true); }
                    break;
                case BreakdownType.Belt:
                    HostFitBelt(100f, out bool _);
                    break;
                default:
                    HostResetBreaker(100f);
                    break;
            }
            Apply();
            Publish();
        }

        // ---------- everybody: one-shot effects (the rest is drawn from the NetworkVariables) ----------

        [Rpc(SendTo.Everyone)]
        private void StartedRpc(byte type)
        {
            Vector3 at = transform.position;
            switch ((BreakdownType)type)
            {
                case BreakdownType.Sputter: Sound?.Invoke(BreakdownSound.Cough, at); break;
                case BreakdownType.Belt: Sound?.Invoke(BreakdownSound.Squeal, at); break;
                default: Sound?.Invoke(BreakdownSound.Bang, at); Sound?.Invoke(BreakdownSound.Spark, at); break;
            }
            BreakdownVisuals.Instance?.PlayStart((BreakdownType)type);
        }

        [Rpc(SendTo.Everyone)]
        private void HitFeedbackRpc(byte result, Vector3 point)
        {
            switch ((HitResult)result)
            {
                case HitResult.Progress: Sound?.Invoke(BreakdownSound.Hit, point); break;
                case HitResult.Fixed: Sound?.Invoke(BreakdownSound.Hit, point); break;
                default: Sound?.Invoke(BreakdownSound.Clang, point); break;
            }
            BreakdownVisuals.Instance?.PlayHit((HitResult)result, point);
        }

        [Rpc(SendTo.Everyone)]
        private void FixedRpc(byte type)
        {
            Sound?.Invoke(BreakdownSound.Fixed, transform.position);
            BreakdownVisuals.Instance?.PlayFixed((BreakdownType)type);
        }

        [Rpc(SendTo.Everyone)]
        private void BeltFittedRpc(int seed)
        {
            Sound?.Invoke(BreakdownSound.BeltFit, transform.position);
            BreakdownVisuals.Instance?.ThrowOldBelt(seed);
        }

        [Rpc(SendTo.Everyone)]
        private void LeverRpc(bool up)
        {
            Sound?.Invoke(up ? BreakdownSound.LeverUp : BreakdownSound.LeverDown, transform.position);
        }

        [Rpc(SendTo.Everyone)]
        private void BreakerFellRpc()
        {
            Sound?.Invoke(BreakdownSound.LeverDown, transform.position);
            Sound?.Invoke(BreakdownSound.Bang, transform.position);
            BreakdownVisuals.Instance?.PlayStart(BreakdownType.Breaker);
        }
    }
}
