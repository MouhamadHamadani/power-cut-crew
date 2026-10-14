using System.Collections.Generic;

namespace PowerCutCrew.Core
{
    /// <summary>
    /// Host-side bookkeeping for "hold E" interactions: a hold starts on a begin request, is kept alive by periodic
    /// keep-alives, and ends on an end request or when no keep-alive arrived for the timeout (a lost end message or a
    /// crashed client must not pour forever). Plain C#, unit tested.
    /// </summary>
    public class HoldKeepAlive
    {
        private readonly Dictionary<(int player, ulong target), double> _lastSeen = new Dictionary<(int, ulong), double>();
        private readonly float _timeout;

        public HoldKeepAlive(float timeoutSeconds)
        {
            _timeout = timeoutSeconds;
        }

        public int Count => _lastSeen.Count;

        public void Begin(int player, ulong target, double now) => _lastSeen[(player, target)] = now;

        /// <summary>Refresh a hold. Returns false when there is no such active hold (it expired or never began).</summary>
        public bool KeepAlive(int player, ulong target, double now)
        {
            if (!_lastSeen.ContainsKey((player, target))) { return false; }
            _lastSeen[(player, target)] = now;
            return true;
        }

        public bool End(int player, ulong target) => _lastSeen.Remove((player, target));

        public bool IsActive(int player, ulong target) => _lastSeen.ContainsKey((player, target));

        /// <summary>Remove and return the holds with no keep-alive for longer than the timeout.</summary>
        public List<(int player, ulong target)> CollectExpired(double now)
        {
            var expired = new List<(int, ulong)>();
            foreach (KeyValuePair<(int player, ulong target), double> pair in _lastSeen)
            {
                if (now - pair.Value > _timeout) { expired.Add(pair.Key); }
            }
            foreach ((int player, ulong target) key in expired) { _lastSeen.Remove(key); }
            return expired;
        }

        public List<(int player, ulong target)> Active()
        {
            var list = new List<(int, ulong)>();
            foreach ((int player, ulong target) key in _lastSeen.Keys) { list.Add(key); }
            return list;
        }

        public List<(int player, ulong target)> RemoveAllForPlayer(int player)
        {
            var removed = new List<(int, ulong)>();
            foreach ((int player, ulong target) key in _lastSeen.Keys)
            {
                if (key.player == player) { removed.Add(key); }
            }
            foreach ((int player, ulong target) key in removed) { _lastSeen.Remove(key); }
            return removed;
        }
    }
}
