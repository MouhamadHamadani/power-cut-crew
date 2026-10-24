using System.Collections.Generic;

namespace PowerCutCrew.Generator
{
    /// <summary>
    /// Minimal power grid: which generator outlet feeds which socket, and how many amps each socket draws.
    /// A socket is powered when a cable is plugged in and the generator is running; the generator's load is the sum of
    /// the draws of the sockets it feeds. Plain C#, unit tested. Placeholder until buildings have real demand.
    /// </summary>
    public class PowerGrid
    {
        private readonly Dictionary<int, float> _draw = new Dictionary<int, float>();
        private readonly Dictionary<int, int> _outletToSocket = new Dictionary<int, int>();
        private readonly HashSet<int> _tripped = new HashSet<int>();

        public void RegisterSocket(int socketId, float ampDraw)
        {
            _draw[socketId] = ampDraw;
        }

        /// <summary>A building's demand changes all the time: set what this socket draws right now (when it is fed).</summary>
        public void SetDraw(int socketId, float ampDraw)
        {
            if (_draw.ContainsKey(socketId)) { _draw[socketId] = ampDraw; }
        }

        public float GetDraw(int socketId) => _draw.TryGetValue(socketId, out float v) ? v : 0f;

        /// <summary>Unplug everything (the street is reset between shifts).</summary>
        public void DisconnectAll() => _outletToSocket.Clear();

        /// <summary>Breakdown hook: a tripped outlet delivers no power (a cable may stay plugged into it). The outlet number is the generator's outlet index.</summary>
        public void SetTripped(int outlet, bool tripped)
        {
            if (tripped) { _tripped.Add(outlet); } else { _tripped.Remove(outlet); }
        }

        public bool IsTripped(int outlet) => _tripped.Contains(outlet);

        /// <summary>The lowest tripped outlet, or -1 when none is tripped.</summary>
        public int TrippedOutlet
        {
            get
            {
                int best = -1;
                foreach (int o in _tripped) { if (best < 0 || o < best) { best = o; } }
                return best;
            }
        }

        /// <summary>Everything is reset to the scene defaults (the street between shifts): nothing is tripped.</summary>
        public void ResetTrips() => _tripped.Clear();

        /// <summary>The outlets that have a cable plugged into a socket, tripped or not.</summary>
        public List<int> ConnectedOutlets()
        {
            var list = new List<int>(_outletToSocket.Keys);
            list.Sort();
            return list;
        }

        /// <summary>Amps the socket behind this outlet asks for (0 when nothing is plugged into it).</summary>
        public float DrawOnOutlet(int outlet) => _outletToSocket.TryGetValue(outlet, out int s) ? GetDraw(s) : 0f;

        /// <summary>The load the generator would see if every plugged outlet fed its socket, tripped or not.</summary>
        public float LoadIfNothingTripped
        {
            get
            {
                float sum = 0f;
                foreach (int s in _outletToSocket.Values) { sum += _draw[s]; }
                return sum;
            }
        }

        /// <summary>A socket is connected as soon as a cable runs to it, whether or not the outlet is tripped.</summary>
        public bool IsConnected(int socketId)
        {
            foreach (int s in _outletToSocket.Values)
            {
                if (s == socketId) { return true; }
            }
            return false;
        }

        /// <summary>Feed a socket from an outlet. False when the socket is unknown or already fed.</summary>
        public bool Connect(int outlet, int socketId)
        {
            if (!_draw.ContainsKey(socketId) || IsConnected(socketId)) { return false; }
            _outletToSocket[outlet] = socketId;
            return true;
        }

        public bool Disconnect(int outlet)
        {
            return _outletToSocket.Remove(outlet);
        }

        /// <summary>A cable runs from a live (not tripped) outlet to this socket.</summary>
        public bool IsFed(int socketId)
        {
            foreach (KeyValuePair<int, int> pair in _outletToSocket)
            {
                if (pair.Value == socketId && !_tripped.Contains(pair.Key)) { return true; }
            }
            return false;
        }

        public bool IsPowered(int socketId, bool generatorRunning) => generatorRunning && IsFed(socketId);

        /// <summary>Amps drawn by the sockets that are currently fed (the load the generator sees when running).</summary>
        public float Load
        {
            get
            {
                float sum = 0f;
                foreach (KeyValuePair<int, int> pair in _outletToSocket)
                {
                    if (!_tripped.Contains(pair.Key)) { sum += _draw[pair.Value]; }
                }
                return sum;
            }
        }

        public int FedCount => _outletToSocket.Count;
    }
}
