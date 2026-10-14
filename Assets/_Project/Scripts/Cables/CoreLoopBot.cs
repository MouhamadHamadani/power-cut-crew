using PowerCutCrew.Core;
using PowerCutCrew.Fuel;
using PowerCutCrew.Generator;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Cables
{
    /// <summary>
    /// Bot behaviour for the core loop (seeded): pull the cord when the generator is off, fetch a can, fill it at the
    /// drum, pour it into the generator, take a plug from a reel, plug it into a socket (sometimes drag it too far or
    /// unplug it). It runs before the base bot; when it has nothing to do it hands the frame back (wander, throw).
    /// </summary>
    public class CoreLoopBot : IBotBehaviour
    {
        private enum Goal { None, PullCord, GetCan, FillCan, PourCan, GetPlug, CarryPlug, DragAway, Unplug }

        private readonly System.Random _rng;
        private readonly bool _preferFull;
        private readonly bool _holdPlug = LaunchArgs.Has("-holdplug");
        private Goal _goal = Goal.None;
        private float _goalTime;
        private float _cooldown;
        private float _idle = 2f;
        private PowerSocket _targetSocket;
        private Vector3 _awayPoint;
        private int _pulls;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            BotExtensions.Factories.Add(seed => new CoreLoopBot(seed));
        }

        public CoreLoopBot(int seed)
        {
            _rng = new System.Random(seed * 31 + 7);
            _preferFull = seed % 2 == 1;   // the others like empty cans: they end up at the drum
        }

        private float R() => (float)_rng.NextDouble();

        private void Set(Goal g)
        {
            if (LaunchArgs.Has("-botlog")) { Debug.Log($"[CoreBot] {_goal} -> {g} t={Time.realtimeSinceStartup:F1}"); }
            _goal = g;
            _goalTime = 0f;
            _pulls = 0;
        }

        public bool Act(PlayerController me, ref PlayerFrame f, float dt)
        {
            GeneratorNet gen = GeneratorNet.Instance;
            if (gen == null || !gen.IsSpawned) { return false; }
            _goalTime += dt;
            _cooldown -= dt;

            if (_holdPlug)
            {
                // test mode (-holdplug): fetch a plug and keep it in hand until somebody kills this process
                NetworkPhysicsItem inHand = BotBrain.HeldByMe();
                if (inHand != null && inHand.Kind == "plug") { return true; }
                if (_goal == Goal.None)
                {
                    _idle -= dt;
                    if (_idle > 0f) { return false; }
                    if (HasPlugToTake()) { Set(Goal.GetPlug); } else { _idle = 1f; return false; }
                }
            }

            if (_goal == Goal.None)
            {
                _idle -= dt;
                if (_idle > 0f) { return false; }
                Choose(gen);
                if (_goal == Goal.None) { _idle = 0.5f + R(); return false; }
            }

            bool done = false;
            switch (_goal)
            {
                case Goal.PullCord: done = PullCord(me, gen, ref f, dt); break;
                case Goal.GetCan: done = GetCan(me, ref f, dt); break;
                case Goal.FillCan: done = FillCan(me, ref f, dt); break;
                case Goal.PourCan: done = PourCan(me, gen, ref f, dt); break;
                case Goal.GetPlug: done = GetPlug(me, ref f, dt); break;
                case Goal.CarryPlug: done = CarryPlug(me, ref f, dt); break;
                case Goal.DragAway: done = DragAway(me, ref f, dt); break;
                case Goal.Unplug: done = Unplug(me, ref f, dt); break;
            }
            if (done || _goalTime > 40f)
            {
                if (_goalTime > 40f && BotBrain.HeldByMe() != null) { f.Drop = true; }
                _goal = Goal.None;
                _idle = 0.3f + R();
            }
            return true;
        }

        // ---------- goal selection ----------

        private void Choose(GeneratorNet gen)
        {
            NetworkPhysicsItem held = BotBrain.HeldByMe();
            if (held != null)
            {
                if (held.Kind == "plug") { Set(Goal.CarryPlug); PickSocket(); return; }
                var can = held.GetComponent<FuelCan>();
                if (can != null) { Set(can.Amount > 3f ? Goal.PourCan : Goal.FillCan); return; }
                return;
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // only the host's bot (it can reach the host-side cable): now and then yank a plugged cable to exercise the pop-out
            if (gen.IsServer && R() < 0.02f)
            {
                foreach (CableNet c in CableNet.All)
                {
                    if (c.State == CableState.Plugged) { c.TestYank(); break; }
                }
            }
#endif
            bool running = gen.State == GeneratorState.Running;
            if (R() < 0.25f && AnyOccupiedSocket(30f) != null) { Set(Goal.Unplug); return; }
            if (!running && gen.State != GeneratorState.Overheated && gen.Fuel > 0.5f && R() < 0.8f) { Set(Goal.PullCord); return; }
            if (gen.Fuel < gen.Capacity * 0.6f && R() < 0.7f) { Set(Goal.GetCan); return; }
            if (R() < 0.6f && HasPlugToTake()) { Set(Goal.GetPlug); return; }
        }

        private static bool HasPlugToTake()
        {
            foreach (CableNet c in CableNet.All)
            {
                if ((c.State == CableState.Fresh || c.State == CableState.Loose) && !c.Item.IsHeld && !c.Item.Locked) { return true; }
            }
            return false;
        }

        private static readonly System.Collections.Generic.Dictionary<int, float> _occupiedSince = new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>A socket that has had a plug in it for at least this long (so two loads get to heat the generator before a bot unplugs).</summary>
        private static PowerSocket AnyOccupiedSocket(float minSeconds = 0f)
        {
            foreach (PowerSocket s in PowerSocket.All)
            {
                if (!s.Occupied) { _occupiedSince.Remove(s.Id); continue; }
                if (!_occupiedSince.TryGetValue(s.Id, out float since)) { _occupiedSince[s.Id] = since = Time.time; }
                if (Time.time - since >= minSeconds) { return s; }
            }
            return null;
        }

        private void PickSocket()
        {
            _targetSocket = null;
            PowerSocket best = null;
            float bestScore = float.MinValue;
            foreach (PowerSocket s in PowerSocket.All)
            {
                if (s.Occupied) { continue; }
                // prefer the hungry sockets (they overload the generator), with some randomness
                float score = s.Id * 0.5f + R() * 3f;
                if (score > bestScore) { bestScore = score; best = s; }
            }
            _targetSocket = best;
            if (R() < (LaunchArgs.Has("-snapbias") ? 0.6f : 0.07f))
            {
                Set(Goal.DragAway);
                float a = R() * Mathf.PI * 2f;
                _awayPoint = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 16f;
            }
        }

        // ---------- goals ----------

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        /// <summary>Walk to a stand point in front of the target, then aim at the target point. Returns true when in place and aimed.</summary>
        private static bool Approach(PlayerController me, Vector3 stand, Vector3 aimAt, ref PlayerFrame f, float dt)
        {
            if (!BotBrain.Walk(me, stand, ref f, dt, 0.5f)) { return false; }
            return BotBrain.AimAt(me, aimAt, ref f, dt) < 6f;
        }

        private bool PressInteract(ref PlayerFrame f, float interval = 0.9f)
        {
            if (_cooldown > 0f) { return false; }
            f.Interact = true;
            _cooldown = interval;
            return true;
        }

        private bool PullCord(PlayerController me, GeneratorNet gen, ref PlayerFrame f, float dt)
        {
            if (gen.State == GeneratorState.Running || gen.State == GeneratorState.Overheated || gen.Fuel <= 0.1f) { return true; }
            Transform cord = gen.CordPoint;
            Vector3 stand = cord.position + Flat(cord.forward).normalized * 1.3f;
            if (Approach(me, stand, cord.position, ref f, dt) && InteractionFocus.Current is GeneratorSwitch)
            {
                if (PressInteract(ref f, 0.8f)) { _pulls++; }
            }
            return _pulls >= 7;
        }

        private bool GetCan(PlayerController me, ref PlayerFrame f, float dt)
        {
            if (BotBrain.HeldByMe() != null) { Set(Goal.None); return true; }
            NetworkPhysicsItem best = null;
            float bestD = float.MaxValue;
            Vector3 pos = me.transform.position;
            foreach (NetworkPhysicsItem i in NetworkPhysicsItem.All)
            {
                var can = i.GetComponent<FuelCan>();
                if (can == null || i.IsHeld || i.Locked) { continue; }
                float d = Vector3.Distance(i.transform.position, pos) - (can.Amount > 5f ? (_preferFull ? 12f : -12f) : 0f) + R();
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best == null) { return true; }
            Vector3 flat = Flat(best.transform.position - pos);
            if (flat.magnitude > 1.8f) { BotBrain.Walk(me, best.transform.position, ref f, dt, 1.8f); return false; }
            BotBrain.AimAt(me, best.transform.position, ref f, dt);
            if (InteractionFocus.Current != null && InteractionFocus.Current.GetComponentInParent<NetworkPhysicsItem>() == best) { PressInteract(ref f, 0.9f); }
            NetworkPhysicsItem held = BotBrain.HeldByMe();
            if (held != null)
            {
                var can = held.GetComponent<FuelCan>();
                Set(can != null && can.Amount > 3f ? Goal.PourCan : Goal.FillCan);
            }
            return false;
        }

        private FuelDrum _drum;

        private bool FillCan(PlayerController me, ref PlayerFrame f, float dt)
        {
            NetworkPhysicsItem held = BotBrain.HeldByMe();
            var can = held != null ? held.GetComponent<FuelCan>() : null;
            if (can == null) { return true; }
            if (can.Amount >= can.Capacity - 0.6f) { Set(Goal.PourCan); return false; }
            if (_drum == null) { _drum = Object.FindFirstObjectByType<FuelDrum>(); }
            if (_drum == null) { return true; }
            Vector3 toMe = Flat(me.transform.position - _drum.transform.position).normalized;
            Vector3 stand = _drum.transform.position + toMe * 1.6f;
            if (Approach(me, stand, _drum.transform.position + Vector3.up * 0.4f, ref f, dt)) { f.InteractHeld = true; }
            return false;
        }

        private GeneratorTank _tank;

        private bool PourCan(PlayerController me, GeneratorNet gen, ref PlayerFrame f, float dt)
        {
            NetworkPhysicsItem held = BotBrain.HeldByMe();
            var can = held != null ? held.GetComponent<FuelCan>() : null;
            if (can == null) { return true; }
            if (can.Amount <= 0.2f || gen.Fuel >= gen.Capacity - 0.3f)
            {
                f.Drop = true;   // empty (or the tank is full): put the can down
                return true;
            }
            if (_tank == null) { _tank = gen.GetComponentInChildren<GeneratorTank>(); }
            Vector3 toMe = Flat(me.transform.position - gen.transform.position).normalized;
            Vector3 stand = gen.transform.position + toMe * 1.5f;
            if (Approach(me, stand, _tank.transform.position, ref f, dt)) { f.InteractHeld = true; }
            return false;
        }

        private bool GetPlug(PlayerController me, ref PlayerFrame f, float dt)
        {
            NetworkPhysicsItem held = BotBrain.HeldByMe();
            if (held != null)
            {
                if (held.Kind == "plug") { Set(Goal.CarryPlug); PickSocket(); }
                return held == null;
            }
            // a fresh cable: use its reel; a loose cable: walk to the plug and pick it up
            CableNet target = null;
            foreach (CableNet c in CableNet.All)
            {
                if ((c.State == CableState.Fresh || c.State == CableState.Loose) && !c.Item.IsHeld && !c.Item.Locked) { target = c; if (R() < 0.5f) { break; } }
            }
            if (target == null) { return true; }
            GeneratorNet gen = GeneratorNet.Instance;
            if (target.State == CableState.Fresh && target.Outlet >= 0)
            {
                Transform outlet = gen.Outlets[target.Outlet];
                Vector3 stand = outlet.position + Flat(outlet.forward).normalized * 1.2f;
                if (Approach(me, stand, outlet.position, ref f, dt)) { PressInteract(ref f, 0.9f); }
                return false;
            }
            Vector3 plugPos = target.transform.position;
            if (Flat(plugPos - me.transform.position).magnitude > 1.7f) { BotBrain.Walk(me, plugPos, ref f, dt, 1.7f); return false; }
            BotBrain.AimAt(me, plugPos, ref f, dt);
            if (InteractionFocus.Current != null && InteractionFocus.Current.GetComponentInParent<NetworkPhysicsItem>() == target.Item) { PressInteract(ref f, 0.9f); }
            return false;
        }

        private bool CarryPlug(PlayerController me, ref PlayerFrame f, float dt)
        {
            NetworkPhysicsItem held = BotBrain.HeldByMe();
            if (held == null || held.Kind != "plug") { return true; }
            if (_targetSocket == null || _targetSocket.Occupied) { PickSocket(); if (_targetSocket == null) { f.Drop = true; return true; } }
            Transform sock = _targetSocket.transform;
            Vector3 stand = sock.position + Flat(sock.forward).normalized * 1.5f;
            if (Approach(me, stand, _targetSocket.PlugPoint.position, ref f, dt)) { PressInteract(ref f, 0.8f); }
            return false;
        }

        private bool DragAway(PlayerController me, ref PlayerFrame f, float dt)
        {
            NetworkPhysicsItem held = BotBrain.HeldByMe();
            if (held == null || held.Kind != "plug") { return true; }   // snapped (the plug was dropped) or let go
            BotBrain.Walk(me, _awayPoint, ref f, dt, 0.5f);
            if (_goalTime > 25f) { f.Drop = true; return true; }
            return false;
        }

        private bool Unplug(PlayerController me, ref PlayerFrame f, float dt)
        {
            NetworkPhysicsItem held = BotBrain.HeldByMe();
            if (held != null)
            {
                if (held.Kind == "plug") { f.Drop = _goalTime > 4f; }
                return held.Kind != "plug" ? true : _goalTime > 4f;
            }
            PowerSocket s = AnyOccupiedSocket();
            if (s == null) { return true; }
            Vector3 stand = s.transform.position + Flat(s.transform.forward).normalized * 1.5f;
            if (Approach(me, stand, s.PlugPoint.position, ref f, dt)) { PressInteract(ref f, 0.8f); }
            return false;
        }
    }
}
