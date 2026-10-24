using PowerCutCrew.Core;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>
    /// Bot behaviour for the breakdowns (seeded), so a bot run exercises the whole flow over the network. A bot that sees a breakdown reacts after a moment and fixes it
    /// through the same input a human gives: <b>sputter</b>: fetch the wrench from its hook, stand at the glowing spot, aim and swing (now and then it aims a little off
    /// and misses on purpose), hang the wrench back; <b>belt</b>: take a spare belt from the crate, carry it to the slot, hold Interact until it is fitted; <b>breaker</b>:
    /// stand at the lever and hold Interact (and when the street is so overloaded that the lever keeps falling back, unplug a cable first). Runs before the core-loop bot.
    /// </summary>
    public class BreakdownBot : IBotBehaviour
    {
        private enum Goal { None, GetWrench, HitSpot, ReturnWrench, GetBelt, FitBelt, ResetBreaker, Unplug }

        // one bot per kind of breakdown in a process (separate processes pick independently)
        private static readonly float[] ClaimedUntil = new float[BreakdownTypes.Count];
        private static readonly int[] ClaimedBy = new int[BreakdownTypes.Count];

        private readonly System.Random _rng;
        private readonly int _id;
        private Goal _goal = Goal.None;
        private BreakdownType _type;
        private float _goalTime;
        private float _cooldown;
        private float _reactAt = -1f;
        private float _swingAt;
        private float _nextLog;
        private Vector3 _aimOffset;
        private bool _missOnPurpose;
        private int _lastHits;
        private int _retrips;
        private float _giveUpUntil;
        private bool _wasLeverUp = true;
        private Interactable _unplugTarget;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            BotExtensions.Factories.Insert(0, seed => new BreakdownBot(seed));
        }

        public BreakdownBot(int seed)
        {
            _rng = new System.Random(seed * 17 + 3);
            _id = seed;
        }

        private float R() => (float)_rng.NextDouble();

        private void Go(Goal g)
        {
            if (LaunchArgs.Has("-botlog")) { Debug.Log($"[BreakdownBot] {_goal} -> {g} t={Time.realtimeSinceStartup:F1}"); }
            _goal = g;
            _goalTime = 0f;
        }

        private bool Claimed(BreakdownType t) => Time.time < ClaimedUntil[(int)t] && ClaimedBy[(int)t] != _id;

        private void Claim(BreakdownType t)
        {
            ClaimedUntil[(int)t] = Time.time + 30f;
            ClaimedBy[(int)t] = _id;
        }

        public bool Act(PlayerController me, ref PlayerFrame f, float dt)
        {
            BreakdownNet net = BreakdownNet.Instance;
            if (net == null || !net.IsSpawned || LaunchArgs.Get("-shot") == "breakdowns") { return false; }   // the doc screenshots finish the breakdowns themselves
            _goalTime += dt;
            _cooldown -= dt;
            _obstacle = net.transform.position;
            NetworkPhysicsItem held = BotBrain.HeldByMe();
            string heldKind = held != null ? held.Kind : null;

            if (_goal == Goal.None)
            {
                BreakdownType? next = Choose(net);
                if (!next.HasValue)
                {
                    _reactAt = -1f;
                    // nothing to fix: do not walk around with the wrench or a belt
                    if (heldKind == "wrench" || heldKind == SpareBelt.Kind) { f.Drop = true; return true; }
                    return false;
                }
                if (_reactAt < 0f) { _reactAt = Time.time + 1.5f + R() * 4f; }
                if (Time.time < _reactAt) { return false; }
                _type = next.Value;
                _reactAt = -1f;
                Claim(_type);
                _swingAt = 0f;
                _missOnPurpose = false;
                _lastHits = net.HitsDone;
                _retrips = 0;
                switch (_type)
                {
                    case BreakdownType.Sputter: Go(heldKind == "wrench" ? Goal.HitSpot : Goal.GetWrench); break;
                    case BreakdownType.Belt: Go(heldKind == SpareBelt.Kind ? Goal.FitBelt : Goal.GetBelt); break;
                    default: Go(Goal.ResetBreaker); break;
                }
            }

            bool done = false;
            switch (_goal)
            {
                case Goal.GetWrench: done = GetWrench(me, ref f, dt, held); break;
                case Goal.HitSpot: done = HitSpot(me, net, ref f, dt, held); break;
                case Goal.ReturnWrench: done = ReturnWrench(me, net, ref f, dt, held); break;
                case Goal.GetBelt: done = GetBelt(me, net, ref f, dt, held); break;
                case Goal.FitBelt: done = FitBelt(me, net, ref f, dt, held); break;
                case Goal.ResetBreaker: done = ResetBreaker(me, net, ref f, dt, held); break;
                case Goal.Unplug: done = Unplug(me, ref f, dt, held); break;
            }
            if (!done && _goalTime > 60f)
            {
                // stuck: let go of what is in hand and leave it alone for a while
                if (held != null) { f.Drop = true; }
                done = true;
                _giveUpUntil = Time.time + 15f;
                Debug.Log($"[BreakdownBot] gave up on {_type}");
            }
            if (done) { _goal = Goal.None; _reactAt = -1f; }
            return true;
        }

        /// <summary>The breakdown this bot should look after, if any (not one somebody else has taken, not one it just gave up on).</summary>
        private BreakdownType? Choose(BreakdownNet net)
        {
            if (Time.time < _giveUpUntil) { return null; }
            for (int i = 0; i < BreakdownTypes.Count; i++)
            {
                var t = (BreakdownType)i;
                if (net.IsActive(t) && !Claimed(t)) { return t; }
            }
            return null;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        /// <summary>-botlog: where the bot is, once a second.</summary>
        private void LogPlace(string what, PlayerController me, Vector3 stand, bool inPlace)
        {
            if (!LaunchArgs.Has("-botlog") || Time.time < _nextLog) { return; }
            _nextLog = Time.time + 1f;
            Debug.Log($"[BreakdownBot] {what}: {Flat(stand - me.transform.position).magnitude:F2} m from the stand point, in place {inPlace}, looking at {(InteractionFocus.Current != null ? InteractionFocus.Current.GetType().Name : "nothing")}");
        }

        private bool PressInteract(ref PlayerFrame f, float interval = 0.9f)
        {
            if (_cooldown > 0f) { return false; }
            f.Interact = true;
            _cooldown = interval;
            return true;
        }

        /// <summary>The generator's footprint: the bot walks around it instead of into it.</summary>
        private Vector3 _obstacle;
        private const float ObstacleRadius = 1.35f;
        private const float DetourRadius = 2.2f;
        private Vector3 _lastPos;
        private float _stuck;
        private float _stuckCheck;

        /// <summary>Where to walk next on the way to a point: straight there, or a point beside the generator when the straight line would cross it (plain C#, unit tested).</summary>
        public static Vector3 NextWaypoint(Vector3 from, Vector3 to, Vector3 center, float blockRadius, float detourRadius)
        {
            Vector3 a = new Vector3(from.x, 0f, from.z), b = new Vector3(to.x, 0f, to.z), c = new Vector3(center.x, 0f, center.z);
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.0001f) { return to; }
            float t = Mathf.Clamp01(Vector3.Dot(c - a, ab) / len2);
            Vector3 closest = a + ab * t;
            if (Vector3.Distance(closest, c) >= blockRadius) { return to; }
            // the way is blocked: go round on the side we are already on
            Vector3 side = Vector3.Cross(Vector3.up, ab.normalized);
            if (Vector3.Dot(a - c, side) < 0f) { side = -side; }
            if (Vector3.Dot(a - c, side) < 0.01f && Vector3.Dot(b - c, side) < 0.01f) { side = Vector3.Cross(Vector3.up, ab.normalized); }
            return c + side * detourRadius;
        }

        /// <summary>Walk to a point on the ground (round the generator, with a hop when stuck), then aim at a target point. True when in place and aimed.</summary>
        private bool Approach(PlayerController me, Vector3 stand, Vector3 aimAt, ref PlayerFrame f, float dt)
        {
            Vector3 way = NextWaypoint(me.transform.position, stand, _obstacle, ObstacleRadius, DetourRadius);
            bool detour = way != stand;
            bool arrived = BotBrain.Walk(me, way, ref f, dt, detour ? 0.6f : 0.35f);
            // stuck: barely moved for a while, so hop and sidestep
            _stuckCheck += dt;
            if (_stuckCheck >= 0.5f)
            {
                Vector3 pos = me.transform.position;
                _stuck = (pos - _lastPos).sqrMagnitude < 0.12f * 0.12f && !arrived ? _stuck + _stuckCheck : 0f;
                _lastPos = pos;
                _stuckCheck = 0f;
            }
            if (_stuck > 1.5f) { f.Jump = true; f.Move.x = _stuck % 3f < 1.5f ? 1f : -1f; }
            if (!arrived || detour) { return false; }
            return BotBrain.AimAt(me, aimAt, ref f, dt) < 4f;
        }

        /// <summary>Walk up to an item lying around, aim at it and pick it up.</summary>
        private bool Fetch(PlayerController me, NetworkPhysicsItem item, ref PlayerFrame f, float dt)
        {
            Vector3 pos = item.transform.position;
            if (Flat(pos - me.transform.position).magnitude > 1.7f)
            {
                BotBrain.Walk(me, NextWaypoint(me.transform.position, pos, _obstacle, ObstacleRadius, DetourRadius), ref f, dt, 0.6f);
                return false;
            }
            BotBrain.AimAt(me, pos, ref f, dt);
            if (InteractionFocus.Current != null && InteractionFocus.Current.GetComponentInParent<NetworkPhysicsItem>() == item) { PressInteract(ref f); }
            return false;
        }

        // ---------- sputter ----------

        private bool GetWrench(PlayerController me, ref PlayerFrame f, float dt, NetworkPhysicsItem held)
        {
            if (held != null)
            {
                if (held.Kind == "wrench") { Go(Goal.HitSpot); return false; }
                f.Drop = true;   // hands full: put it down first
                return false;
            }
            WrenchItem wrench = WrenchItem.Instance;
            if (wrench == null || wrench.Item.IsHeld) { return !BreakdownNet.Instance.IsActive(BreakdownType.Sputter) || wrench == null; }
            return Fetch(me, wrench.Item, ref f, dt);
        }

        private bool HitSpot(PlayerController me, BreakdownNet net, ref PlayerFrame f, float dt, NetworkPhysicsItem held)
        {
            if (!net.IsActive(BreakdownType.Sputter)) { Go(Goal.ReturnWrench); return false; }
            if (held == null || held.Kind != "wrench") { Go(Goal.GetWrench); return false; }
            Transform spot = net.SweetSpots[Mathf.Clamp(net.SweetSpotIndex, 0, net.SweetSpots.Count - 1)];
            Vector3 normal = Flat(net.transform.rotation * new Vector3(0f, 0f, spot.localPosition.z < 0f ? -1f : 1f)).normalized;
            Vector3 stand = new Vector3(spot.position.x, 0f, spot.position.z) + normal * 1.1f;
            if (net.HitsDone != _lastHits) { _lastHits = net.HitsDone; _missOnPurpose = false; }
            Vector3 aim = spot.position + (_missOnPurpose ? _aimOffset : Vector3.zero);
            bool inPlace = Approach(me, stand, aim, ref f, dt);
            if (LaunchArgs.Has("-botlog") && Time.time > _nextLog)
            {
                _nextLog = Time.time + 1f;
                Debug.Log($"[BreakdownBot] hit: {Flat(stand - me.transform.position).magnitude:F2} m from the stand point, in place {inPlace}, spot {net.SweetSpotIndex}, hits {net.HitsDone}");
            }
            if (!inPlace) { return false; }
            if (Time.time < _swingAt) { return false; }
            f.Throw = true;   // the use button: swing the wrench
            _swingAt = Time.time + 0.6f;
            // now and then the next swing goes a little wide (the sweet spot is only about 25 cm across)
            if (R() < 0.25f)
            {
                _missOnPurpose = true;
                Vector3 side = Vector3.Cross(Vector3.up, normal) * (R() < 0.5f ? -1f : 1f);
                _aimOffset = side * (0.4f + R() * 0.15f);
                if (LaunchArgs.Has("-botlog")) { Debug.Log("[BreakdownBot] the next swing goes wide"); }
            }
            else { _missOnPurpose = false; }
            return false;
        }

        private bool ReturnWrench(PlayerController me, BreakdownNet net, ref PlayerFrame f, float dt, NetworkPhysicsItem held)
        {
            if (held == null || held.Kind != "wrench") { return true; }
            // hang it back: stand a few steps from the hook, look at it and toss the wrench that way
            Transform hook = net.WrenchHook != null ? net.WrenchHook : net.transform;
            Vector3 away = Flat(me.transform.position - hook.position);
            if (away.sqrMagnitude < 0.01f) { away = Vector3.back; }
            Vector3 stand = hook.position + away.normalized * 1.6f;
            stand.y = 0f;
            if (!Approach(me, stand, hook.position, ref f, dt)) { return false; }
            f.Drop = true;
            return true;
        }

        // ---------- belt ----------

        private bool GetBelt(PlayerController me, BreakdownNet net, ref PlayerFrame f, float dt, NetworkPhysicsItem held)
        {
            if (!net.IsActive(BreakdownType.Belt)) { if (held != null) { f.Drop = true; } return true; }
            if (held != null)
            {
                if (held.Kind == SpareBelt.Kind) { Go(Goal.FitBelt); return false; }
                f.Drop = true;
                return false;
            }
            // a belt that lies around is as good as a new one
            foreach (SpareBelt b in SpareBelt.All)
            {
                if (b != null && b.Item != null && b.Item.IsSpawned && !b.Item.IsHeld && b.Item.NetworkManager == net.NetworkManager) { return Fetch(me, b.Item, ref f, dt); }
            }
            var crate = Object.FindFirstObjectByType<SparePartsCrate>();
            if (crate == null) { return true; }
            Vector3 toMe = Flat(me.transform.position - crate.transform.position);
            if (toMe.sqrMagnitude < 0.01f) { toMe = Vector3.forward; }
            Vector3 stand = crate.transform.position + toMe.normalized * 1.3f;
            stand.y = 0f;
            if (Approach(me, stand, crate.transform.position + Vector3.up * 0.3f, ref f, dt) && InteractionFocus.Current is SparePartsCrate) { PressInteract(ref f); }
            return false;
        }

        private bool FitBelt(PlayerController me, BreakdownNet net, ref PlayerFrame f, float dt, NetworkPhysicsItem held)
        {
            if (!net.IsActive(BreakdownType.Belt)) { if (held != null) { f.Drop = true; } return true; }
            if (held == null || held.Kind != SpareBelt.Kind) { Go(Goal.GetBelt); return false; }
            Transform slot = net.BeltSlot;
            Vector3 normal = Flat(net.transform.rotation * Vector3.forward).normalized;
            Vector3 stand = new Vector3(slot.position.x, 0f, slot.position.z) + normal * 1.2f;
            bool inPlace = Approach(me, stand, slot.position, ref f, dt);
            LogPlace("slot", me, stand, inPlace);
            if (!inPlace) { return false; }
            if (InteractionFocus.Current is BeltSlot) { f.InteractHeld = true; }
            return false;
        }

        // ---------- breaker ----------

        private bool ResetBreaker(PlayerController me, BreakdownNet net, ref PlayerFrame f, float dt, NetworkPhysicsItem held)
        {
            if (!net.IsActive(BreakdownType.Breaker)) { if (held != null) { f.Drop = true; } return true; }
            if (held != null) { f.Drop = true; return false; }   // hands free for the lever
            // the lever fell back after a reset: the street is overloaded
            bool up = net.LeverUp;
            if (_wasLeverUp && !up && _goalTime > 3f) { _retrips++; }
            _wasLeverUp = up;
            if (_retrips >= 2)
            {
                Interactable socket = FindUnpluggable(me);
                if (socket != null) { _unplugTarget = socket; _retrips = 0; Go(Goal.Unplug); return false; }
            }
            Transform lever = net.Lever;
            Vector3 normal = Flat(net.transform.rotation * Vector3.forward).normalized;
            Vector3 stand = new Vector3(lever.position.x, 0f, lever.position.z) + normal * 1.6f;   // the lever sits on top of the housing: stand clear of its front
            bool inPlace = Approach(me, stand, lever.position + Vector3.up * 0.05f, ref f, dt);
            LogPlace("lever", me, stand, inPlace);
            if (!inPlace) { return false; }
            if (InteractionFocus.Current is BreakerLever) { f.InteractHeld = true; }
            return false;
        }

        /// <summary>A socket with a cable in it (its interactable offers to unplug it), found without knowing the cable assembly: by what the prompt says.</summary>
        private static Interactable FindUnpluggable(PlayerController me)
        {
            Interactable best = null;
            float bestD = float.MaxValue;
            foreach (Interactable i in Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None))
            {
                if (i == null || i.CurrentPromptKey != "cable.unplug") { continue; }
                float d = Vector3.Distance(i.transform.position, me.transform.position);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Relieve the generator: pull a plug out of a socket so the next reset holds.</summary>
        private bool Unplug(PlayerController me, ref PlayerFrame f, float dt, NetworkPhysicsItem held)
        {
            if (held != null)
            {
                if (_goalTime > 2f) { f.Drop = true; return true; }   // the plug is in hand: let it hang
                return false;
            }
            if (_unplugTarget == null || _unplugTarget.CurrentPromptKey != "cable.unplug") { return true; }
            Vector3 at = _unplugTarget.transform.position;
            Vector3 toMe = Flat(me.transform.position - at);
            if (toMe.sqrMagnitude < 0.01f) { toMe = Vector3.forward; }
            Vector3 stand = at + toMe.normalized * 1.5f;
            stand.y = 0f;
            if (Approach(me, stand, at, ref f, dt) && InteractionFocus.Current == _unplugTarget) { PressInteract(ref f, 0.8f); }
            return _goalTime > 20f;
        }
    }
}
