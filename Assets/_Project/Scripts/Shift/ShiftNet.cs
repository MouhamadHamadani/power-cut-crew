using System;
using PowerCutCrew.Breakdowns;
using PowerCutCrew.Buildings;
using PowerCutCrew.Cables;
using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Shift
{
    /// <summary>The numbers on the summary screen, as a NetworkVariable (written once when the shift ends).</summary>
    public struct ShiftSummaryData : INetworkSerializable, IEquatable<ShiftSummaryData>
    {
        public float UptimePercent;
        public int Money;
        public int AngryBuildings;
        public int Overheats;
        public int Snaps;
        public int FailedPulls;
        public byte Verdict;
        public int BreakdownsFixed;
        public float AvgFixSeconds;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref UptimePercent);
            serializer.SerializeValue(ref Money);
            serializer.SerializeValue(ref AngryBuildings);
            serializer.SerializeValue(ref Overheats);
            serializer.SerializeValue(ref Snaps);
            serializer.SerializeValue(ref FailedPulls);
            serializer.SerializeValue(ref Verdict);
            serializer.SerializeValue(ref BreakdownsFixed);
            serializer.SerializeValue(ref AvgFixSeconds);
        }

        public bool Equals(ShiftSummaryData o)
        {
            return UptimePercent.Equals(o.UptimePercent) && Money == o.Money && AngryBuildings == o.AngryBuildings && Overheats == o.Overheats
                && Snaps == o.Snaps && FailedPulls == o.FailedPulls && Verdict == o.Verdict && BreakdownsFixed == o.BreakdownsFixed && AvgFixSeconds.Equals(o.AvgFixSeconds);
        }
    }

    /// <summary>
    /// The shift on the network. The host runs the <see cref="ShiftModel"/> (Lobby, countdown, Running, Summary) and the <see cref="BuildingDirector"/>
    /// and owns the crew's money; everything goes out as NetworkVariables (state and countdown at once, time left 4 Hz, money whenever it changes),
    /// so a late joiner sees the right time, money and buildings. The same object draws the HUD and the summary on every machine.
    /// </summary>
    public class ShiftNet : NetworkBehaviour
    {
        /// <summary>The shift of this process (the host's when two network managers live in one process, as in tests).</summary>
        public static ShiftNet Instance { get; private set; }

        [SerializeField] private ShiftHud _hudPrefab;

        private readonly NetworkVariable<byte> _state = new NetworkVariable<byte>((byte)ShiftState.Lobby);
        private readonly NetworkVariable<float> _timeLeft = new NetworkVariable<float>(600f);
        private readonly NetworkVariable<int> _countdown = new NetworkVariable<int>(0);
        private readonly NetworkVariable<int> _money = new NetworkVariable<int>(0);
        private readonly NetworkVariable<int> _shiftNumber = new NetworkVariable<int>(0);
        private readonly NetworkVariable<ShiftSummaryData> _summary = new NetworkVariable<ShiftSummaryData>();

        private ShiftModel _model;
        private BuildingDirector _director;
        private float _moneyExact;
        private int _baseOverheats, _baseSnaps, _baseFailedPulls;
        private float _nextTimeSync;
        private ShiftHud _hud;
        private int _shiftsStarted;

        public ShiftState State => IsServer && _model != null ? _model.State : (ShiftState)_state.Value;
        public float TimeLeft => IsServer && _model != null ? _model.TimeLeft : _timeLeft.Value;
        public int Countdown => IsServer && _model != null ? _model.CountdownNumber : _countdown.Value;
        public int Money => _money.Value;
        public int ShiftNumber => _shiftNumber.Value;
        public ShiftSummaryData Summary => _summary.Value;
        public ShiftHud Hud => _hud;
        public float Duration => ShiftTuning.Instance.Duration;

        /// <summary>Host: the buildings' rules, while a shift runs.</summary>
        public BuildingDirector Director => _director;

        public override void OnNetworkSpawn()
        {
            if (IsServer || Instance == null || !Instance.IsServer) { Instance = this; }
            if (IsServer)
            {
                _model = new ShiftModel(ShiftTuning.Instance.Duration, ShiftTuning.Instance.CountdownSeconds);
                _timeLeft.Value = _model.TimeLeft;
            }
            if (IsServer) { BreakdownNet.Started += OnBreakdownStarted; }
            if (_hudPrefab != null)
            {
                _hud = Instantiate(_hudPrefab);
                _hud.Bind(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) { BreakdownNet.Started -= OnBreakdownStarted; }
            if (Instance == this) { Instance = null; }
            if (_hud != null) { Destroy(_hud.gameObject); }
        }

        // ---------- host actions ----------

        /// <summary>Host: somebody pressed "Start shift" at the clipboard.</summary>
        public bool HostStart()
        {
            if (!IsServer || _model == null || !_model.Start()) { return false; }
            _shiftsStarted++;
            _shiftNumber.Value = _shiftsStarted;
            Debug.Log($"[Shift] starting shift {_shiftsStarted}: countdown");
            PublishState(true);
            return true;
        }

        /// <summary>Host: "Another shift": the street goes back to the scene defaults and everybody is back in the lobby.</summary>
        public bool HostAnotherShift()
        {
            if (!IsServer || _model == null || _model.State != ShiftState.Summary) { return false; }
            ResetEverything();
            return true;
        }

        private void ResetEverything()
        {
            if (_director != null) { _director.End(); _director = null; }
            foreach (BuildingNet b in BuildingNet.All)
            {
                if (b != null && b.IsServer && b.Model != null) { b.Model.Stop(); b.HostClearBubble(); b.HostPublishNow(); }
            }
            CoreLoopWorld.ResetStreet();
            if (BreakdownNet.Instance != null) { BreakdownNet.Instance.HostReset(); }
            _model.Reset();
            _moneyExact = 0f;
            _money.Value = 0;
            _summary.Value = default;
            _timeLeft.Value = _model.TimeLeft;
            NetMetrics.Count("shiftResets");
            int heldAfter = 0, bubblesAfter = 0;
            foreach (NetworkPhysicsItem item in NetworkPhysicsItem.All) { if (item != null && item.IsHeld) { heldAfter++; } }
            foreach (BuildingNet b in BuildingNet.All) { if (b != null && b.IsSpawned && b.Model != null && b.HasBubble) { bubblesAfter++; } }
            Debug.Log($"[Shift] back in the lobby (street reset): money {_money.Value}, held items {heldAfter}, bubbles {bubblesAfter}");
            PublishState(true);
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _model == null) { return; }
            float dt = Time.fixedDeltaTime;
            ShiftEvent e = _model.Tick(dt);
            if ((e & ShiftEvent.Started) != 0) { BeginShift(); }
            if (_model.State == ShiftState.Running && _director != null)
            {
                _moneyExact += _director.Tick(dt);
                int whole = Mathf.FloorToInt(_moneyExact);
                if (whole != _money.Value) { _money.Value = whole; }
            }
            if ((e & ShiftEvent.Ended) != 0) { EndShift(); }
            PublishState(e != ShiftEvent.None);
        }

        private void BeginShift()
        {
            _director = new BuildingDirector(LaunchArgs.Seed * 31 + 7);
            _director.Begin();
            _bubbleRng = new System.Random(LaunchArgs.Seed * 71 + 5);
            if (BreakdownNet.Instance != null && BreakdownNet.Instance.IsServer) { BreakdownNet.Instance.HostBeginShift(); }
            _moneyExact = 0f;
            _money.Value = 0;
            _baseOverheats = NetMetrics.GetCount("overheats");
            _baseSnaps = NetMetrics.GetCount("snaps");
            _baseFailedPulls = NetMetrics.GetCount("pullsFail");
            int bubbles = 0, held = 0;
            var happy = new System.Text.StringBuilder();
            foreach (BuildingNet b in BuildingNet.All)
            {
                if (b == null || !b.IsSpawned) { continue; }
                if (b.HasBubble) { bubbles++; }
                happy.Append(b.Happiness.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            }
            foreach (NetworkPhysicsItem item in NetworkPhysicsItem.All)
            {
                if (item != null && item.IsHeld) { held++; }
            }
            string breakdownState = "";
            BreakdownNet breakdowns = BreakdownNet.Instance;
            if (breakdowns != null && breakdowns.IsSpawned)
            {
                WrenchItem wrench = WrenchItem.Instance;
                float fromHook = wrench != null && breakdowns.WrenchHook != null ? Vector3.Distance(wrench.transform.position, breakdowns.WrenchHook.position) : -1f;
                breakdownState = $", breakdowns active {breakdowns.ActiveCount}, loose belts {SpareBelt.CountIn(NetworkManager)}, wrench {fromHook:F2} m from its hook";
            }
            Debug.Log($"[Shift] start state: money {_money.Value}, happiness {happy}, bubbles {bubbles}, held items {held}{breakdownState}");
            Debug.Log($"[Shift] the power cut begins ({_model.Duration:F0} s, {_director.Buildings.Count} buildings)");
        }

        private void EndShift()
        {
            ShiftTuning t = ShiftTuning.Instance;
            float uptime = _director != null ? _director.Group.UptimePercent : 100f;
            BreakdownNet breakdowns = BreakdownNet.Instance != null && BreakdownNet.Instance.IsServer ? BreakdownNet.Instance : null;
            var summary = new ShiftSummaryData
            {
                BreakdownsFixed = breakdowns != null ? breakdowns.FixedCount : 0,
                AvgFixSeconds = breakdowns != null ? Mathf.Round(breakdowns.AverageFixSeconds * 10f) / 10f : 0f,
                UptimePercent = Mathf.Round(uptime * 10f) / 10f,
                Money = Mathf.FloorToInt(_moneyExact),
                AngryBuildings = _director != null ? _director.Group.AngryCount : 0,
                Overheats = NetMetrics.GetCount("overheats") - _baseOverheats,
                Snaps = NetMetrics.GetCount("snaps") - _baseSnaps,
                FailedPulls = NetMetrics.GetCount("pullsFail") - _baseFailedPulls,
                Verdict = (byte)ShiftModel.Verdict(uptime, t.LovedFrom, t.GoodFrom, t.MehFrom),
            };
            _summary.Value = summary;
            _money.Value = summary.Money;
            NetMetrics.Sample("shiftUptimePct", summary.UptimePercent);
            NetMetrics.Sample("shiftMoney", summary.Money);
            NetMetrics.Sample("shiftAngry", summary.AngryBuildings);
            if (breakdowns != null) { breakdowns.HostEndShift(); }
            Debug.Log($"[Shift] over: uptime {summary.UptimePercent:F1} %, money {summary.Money}, angry {summary.AngryBuildings}, overheats {summary.Overheats}, snaps {summary.Snaps}, failed pulls {summary.FailedPulls}, breakdowns fixed {summary.BreakdownsFixed} (average fix {summary.AvgFixSeconds:F1} s)");
            if (_director != null) { _director.End(); _director = null; }
        }

        private System.Random _bubbleRng = new System.Random(5);

        /// <summary>Host: a breakdown started, so a neighbour shouts about it a moment later (one of the buildings that want power, or any).</summary>
        private void OnBreakdownStarted(BreakdownType type)
        {
            StartCoroutine(NeighbourLine(type));
        }

        private System.Collections.IEnumerator NeighbourLine(BreakdownType type)
        {
            yield return new WaitForSeconds(BreakdownTuning.Instance.NeighbourLineDelay);
            if (_model == null || _model.State != ShiftState.Running) { yield break; }
            var candidates = new System.Collections.Generic.List<BuildingNet>();
            foreach (BuildingNet b in BuildingNet.All)
            {
                if (b != null && b.IsServer && b.IsSpawned && b.Model != null && b.Requesting && !b.HasBubble) { candidates.Add(b); }
            }
            if (candidates.Count == 0)
            {
                foreach (BuildingNet b in BuildingNet.All)
                {
                    if (b != null && b.IsServer && b.IsSpawned && b.Model != null) { candidates.Add(b); }
                }
            }
            if (candidates.Count == 0) { yield break; }
            BuildingNet speaker = candidates[_bubbleRng.Next(candidates.Count)];
            speaker.HostShowBubble((BubbleTrigger)((int)BubbleTrigger.Sputter + (int)type), _bubbleRng);   // the triggers follow the breakdown types: sputter, belt, breaker
        }

        private void PublishState(bool force)
        {
            byte state = (byte)_model.State;
            if (_state.Value != state) { _state.Value = state; }
            if (_countdown.Value != _model.CountdownNumber) { _countdown.Value = _model.CountdownNumber; }
            if (!force && Time.unscaledTime < _nextTimeSync) { return; }
            _nextTimeSync = Time.unscaledTime + 1f / Mathf.Max(0.1f, ShiftTuning.Instance.SyncRateHz);
            float shown = Mathf.Ceil(_model.TimeLeft * 4f) / 4f;
            if (!Mathf.Approximately(shown, _timeLeft.Value)) { _timeLeft.Value = shown; }
        }
    }
}
