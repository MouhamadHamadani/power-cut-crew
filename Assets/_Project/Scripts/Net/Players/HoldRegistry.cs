using System.Collections.Generic;
using PowerCutCrew.Core;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// Host: drives "hold E" interactions (pouring, filling). The owner sends begin, periodic keep-alives and end;
    /// the host ticks the interactable every frame while the hold is alive, and ends it when the keep-alives stop
    /// (<see cref="HoldKeepAlive"/>), the player leaves, or the interactable says it is done.
    /// </summary>
    public class HoldRegistry : MonoBehaviour
    {
        private struct Entry
        {
            public NetPlayer Player;
            public Interactable Target;
        }

        public static HoldRegistry Instance { get; private set; }

        private HoldKeepAlive _hold;
        private readonly Dictionary<(int player, ulong target), Entry> _entries = new Dictionary<(int, ulong), Entry>();

        private void Awake()
        {
            Instance = this;
            _hold = new HoldKeepAlive(NetTuning.Instance.HoldKeepAliveTimeout);
        }

        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; }
        }

        public int ActiveCount => _entries.Count;

        public void Handle(NetPlayer player, Interactable target, ulong key, byte phase, InteractionContext context)
        {
            int id = player.PlayerId;
            double now = Time.unscaledTimeAsDouble;
            switch (phase)
            {
                case NetPlayer.HoldBegin:
                    Begin(player, target, key, context, now);
                    break;
                case NetPlayer.HoldKeepAlive:
                    // a keep-alive for a hold that expired (or whose begin was lost) restarts it: the player is still holding E
                    if (!_hold.KeepAlive(id, key, now)) { Begin(player, target, key, context, now); }
                    break;
                case NetPlayer.HoldEnd:
                    if (_hold.End(id, key)) { End(id, key, context); }
                    break;
            }
        }

        private void Begin(NetPlayer player, Interactable target, ulong key, InteractionContext context, double now)
        {
            if (_entries.ContainsKey((player.PlayerId, key)) || !target.HostHoldBegin(context)) { return; }
            _hold.Begin(player.PlayerId, key, now);
            _entries[(player.PlayerId, key)] = new Entry { Player = player, Target = target };
        }

        private void End(int player, ulong key, InteractionContext context)
        {
            if (_entries.TryGetValue((player, key), out Entry e))
            {
                _entries.Remove((player, key));
                if (e.Target != null) { e.Target.HostHoldEnd(context); }
            }
        }

        public void EndAllFor(int player)
        {
            foreach ((int p, ulong key) k in _hold.RemoveAllForPlayer(player)) { End(k.p, k.key, new InteractionContext(player, Vector3.zero)); }
        }

        private void Update()
        {
            if (_entries.Count == 0) { return; }
            foreach ((int p, ulong key) k in _hold.CollectExpired(Time.unscaledTimeAsDouble))
            {
                NetMetrics.Count("holdExpired");
                End(k.p, k.key, new InteractionContext(k.p, Vector3.zero));
            }
            var finished = new List<(int, ulong)>();
            foreach (KeyValuePair<(int player, ulong target), Entry> pair in _entries)
            {
                Entry e = pair.Value;
                if (e.Player == null || e.Target == null) { finished.Add(pair.Key); continue; }
                var ctx = new InteractionContext(pair.Key.player, e.Player.transform.position);
                if (!e.Target.HostHoldTick(ctx, Time.deltaTime)) { finished.Add(pair.Key); }
            }
            foreach ((int player, ulong target) key in finished)
            {
                _hold.End(key.player, key.target);
                if (_entries.TryGetValue(key, out Entry e))
                {
                    _entries.Remove(key);
                    if (e.Target != null) { e.Target.HostHoldEnd(new InteractionContext(key.player, Vector3.zero)); }
                }
            }
        }
    }
}
