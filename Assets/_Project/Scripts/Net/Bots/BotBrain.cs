using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// Seeded bot (-bot). Drives the same PlayerFrame a human produces: wanders, walks up to a free item, aims at it
    /// and presses interact (so the raycast, prompt and request path are exercised), carries it, then drops or throws it.
    /// Sometimes walks through the physics chain. -holdforever makes it keep the first item it grabs.
    /// </summary>
    public class BotBrain : IPlayerInput
    {
        private enum State { Pause, Wander, ToItem, Carry, ThrowAim, Chain, HoldForever }

        private readonly System.Random _rng;
        private readonly bool _holdForever = LaunchArgs.Has("-holdforever");
        private readonly bool _noGrab = LaunchArgs.Has("-nograb");
        private State _state = State.Pause;
        private float _timer = 2f;
        private float _stateTime;
        private float _cooldown;
        private Vector3 _target;
        private NetworkPhysicsItem _item;
        private Vector3 _lastPos;
        private float _stuck;
        private float _checkTimer;

        private readonly System.Collections.Generic.List<IBotBehaviour> _extensions = new System.Collections.Generic.List<IBotBehaviour>();

        public BotBrain(int seed)
        {
            _rng = new System.Random(seed);
            foreach (System.Func<int, IBotBehaviour> make in BotExtensions.Factories) { _extensions.Add(make(seed)); }
        }

        private float R() => (float)_rng.NextDouble();

        private void Go(State s, float timer = 0f)
        {
            if (LaunchArgs.Has("-botlog")) { Debug.Log($"[Bot] {_state} -> {s} t={Time.realtimeSinceStartup:F1}"); }
            _state = s;
            _timer = timer;
            _stateTime = 0f;
        }

        public PlayerFrame Read(PlayerController me, float dt)
        {
            var f = new PlayerFrame();
            foreach (IBotBehaviour extension in _extensions)
            {
                if (extension.Act(me, ref f, dt)) { return f; }
            }
            _stateTime += dt;
            _cooldown -= dt;
            Vector3 pos = me.transform.position;
            NetworkPhysicsItem held = HeldByMe();

            // stuck = barely moved over a half-second window (independent of the frame rate)
            _checkTimer += dt;
            if (_checkTimer >= 0.5f)
            {
                _stuck = (pos - _lastPos).sqrMagnitude < 0.15f * 0.15f ? _stuck + _checkTimer : 0f;
                _lastPos = pos;
                _checkTimer = 0f;
            }
            bool walking = _state == State.Wander || _state == State.ToItem || _state == State.Chain || _state == State.Carry;
            if (walking && _stuck > 1.5f)
            {
                _stuck = 0f;
                f.Jump = true;
                if (_stateTime > 4f) { Go(State.Pause, 0.5f); }
            }

            switch (_state)
            {
                case State.Pause:
                    _timer -= dt;
                    if (_timer <= 0f) { PickGoal(held); }
                    break;

                case State.Wander:
                    if (Walk(me, _target, ref f, dt, 1.5f) || _stateTime > 12f) { Go(State.Pause, 1f + R() * 2f); }
                    break;

                case State.ToItem:
                    if (held != null)
                    {
                        if (_holdForever) { Go(State.HoldForever); }
                        else { _target = RandomPoint(me.transform.position, 8f); Go(State.Carry, 4f + R() * 4f); }
                        break;
                    }
                    if (_item == null || _item.IsHeld || _stateTime > 25f) { Go(State.Pause, 0.5f); break; }
                    ApproachAndGrab(me, ref f, dt);
                    break;

                case State.Carry:
                    if (held == null) { Go(State.Pause, 1f); break; }
                    _timer -= dt;
                    Walk(me, _target, ref f, dt, 1f);
                    if (_timer <= 0f)
                    {
                        if (R() < 0.4f) { _target = RandomPoint(Vector3.zero, 12f); Go(State.ThrowAim, 3f); }
                        else { f.Drop = true; Go(State.Pause, 1f + R() * 2f); }
                    }
                    break;

                case State.ThrowAim:
                    if (held == null) { Go(State.Pause, 1f); break; }
                    _timer -= dt;
                    Vector3 aimPoint = new Vector3(_target.x, me.CameraTarget.position.y + 2f, _target.z);
                    float err = AimAt(me, aimPoint, ref f, dt);
                    if ((err < 8f || _timer <= 0f) && _cooldown <= 0f)
                    {
                        f.Throw = true;
                        _cooldown = 1f;
                        Go(State.Pause, 1f + R() * 2f);
                    }
                    break;

                case State.Chain:
                    if (WorldAnchors.Instance == null) { Go(State.Pause, 1f); break; }
                    if (Walk(me, _target, ref f, dt, 0.8f)) { _target = WorldAnchors.Instance.ChainPosition + new Vector3((R() - 0.5f) * 3f, 0f, (R() - 0.5f) * 3f); }
                    if (_stateTime > 14f) { Go(State.Pause, 1f); }
                    break;

                case State.HoldForever:
                    break;
            }
            return f;
        }

        private void PickGoal(NetworkPhysicsItem held)
        {
            if (held != null)
            {
                if (_holdForever) { Go(State.HoldForever); }
                else { Go(State.Carry, 2f); _target = RandomPoint(Vector3.zero, 8f); }
                return;
            }
            float roll = R();
            if (_holdForever || roll < 0.65f)
            {
                _item = NearestFreeItem();
                if (_item != null) { Go(State.ToItem); return; }
            }
            if (roll < 0.8f && WorldAnchors.Instance != null)
            {
                _target = WorldAnchors.Instance.ChainPosition;
                Go(State.Chain);
                return;
            }
            _target = RandomPoint(Vector3.zero, 14f);
            Go(State.Wander);
        }

        private void ApproachAndGrab(PlayerController me, ref PlayerFrame f, float dt)
        {
            Vector3 itemPos = _item.transform.position;
            Vector3 flat = itemPos - me.transform.position;
            flat.y = 0f;
            if (flat.magnitude > 2.0f)
            {
                Walk(me, itemPos, ref f, dt, 2.0f);
                return;
            }
            AimAt(me, itemPos, ref f, dt);
            Core.Interactable focus = Core.InteractionFocus.Current;
            if (!_noGrab && focus != null && focus.GetComponentInParent<NetworkPhysicsItem>() == _item && _cooldown <= 0f)
            {
                f.Interact = true;
                _cooldown = 1f;
            }
        }

        public static NetworkPhysicsItem HeldByMe()
        {
            foreach (NetworkPhysicsItem i in NetworkPhysicsItem.All)
            {
                if (i.HeldByLocalClient) { return i; }
            }
            return null;
        }

        private NetworkPhysicsItem NearestFreeItem()
        {
            NetworkPhysicsItem best = null;
            float bestD = float.MaxValue;
            Vector3 me = NetPlayer.Local != null ? NetPlayer.Local.transform.position : Vector3.zero;
            foreach (NetworkPhysicsItem i in NetworkPhysicsItem.All)
            {
                if (i.IsHeld || i.IgnoredByWanderBots) { continue; }
                float d = Vector3.Distance(i.transform.position, me) + R() * 4f;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private Vector3 RandomPoint(Vector3 center, float radius)
        {
            float a = R() * Mathf.PI * 2f;
            float r = Mathf.Sqrt(R()) * radius;
            Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            p.x = Mathf.Clamp(p.x, -17f, 17f);
            p.z = Mathf.Clamp(p.z, -17f, 17f);
            return p;
        }

        /// <summary>Turn (yaw and pitch) towards a world point; returns the remaining angle error in degrees.</summary>
        public static float AimAt(PlayerController me, Vector3 point, ref PlayerFrame f, float dt)
        {
            float rate = 360f * dt;
            Vector3 eye = me.CameraTarget.position;
            Vector3 d = point - eye;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            float wantYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float yawDiff = Mathf.DeltaAngle(me.transform.eulerAngles.y, wantYaw);
            float wantPitch = -Mathf.Atan2(d.y, Mathf.Max(0.01f, flat.magnitude)) * Mathf.Rad2Deg;
            float pitchDiff = wantPitch - me.Pitch;
            f.YawDelta = Mathf.Clamp(yawDiff, -rate, rate);
            f.PitchDelta = Mathf.Clamp(pitchDiff, -rate, rate);
            return Mathf.Abs(yawDiff) + Mathf.Abs(pitchDiff);
        }

        /// <summary>Walk towards a point on the ground; returns true when within arrive distance.</summary>
        public static bool Walk(PlayerController me, Vector3 target, ref PlayerFrame f, float dt, float arrive)
        {
            Vector3 d = target - me.transform.position;
            d.y = 0f;
            if (d.magnitude <= arrive) { return true; }
            float want = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float diff = Mathf.DeltaAngle(me.transform.eulerAngles.y, want);
            float rate = 360f * dt;
            f.YawDelta = Mathf.Clamp(diff, -rate, rate);
            // look level while walking
            f.PitchDelta = Mathf.Clamp(-me.Pitch, -rate, rate);
            f.Move = Mathf.Abs(diff) < 50f ? new Vector2(0f, 1f) : Vector2.zero;
            return false;
        }
    }
}
