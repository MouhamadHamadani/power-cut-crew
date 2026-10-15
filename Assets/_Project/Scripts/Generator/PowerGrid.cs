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

        /// <summary>Feed a socket from an outlet. False when the socket is unknown or already fed.</summary>
        public bool Connect(int outlet, int socketId)
        {
            if (!_draw.ContainsKey(socketId) || IsFed(socketId)) { return false; }
            _outletToSocket[outlet] = socketId;
            return true;
        }

        public bool Disconnect(int outlet)
        {
            return _outletToSocket.Remove(outlet);
        }

        public bool IsFed(int socketId)
        {
            foreach (int s in _outletToSocket.Values)
            {
                if (s == socketId) { return true; }
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
                foreach (int s in _outletToSocket.Values) { sum += _draw[s]; }
                return sum;
            }
        }

        public int FedCount => _outletToSocket.Count;
    }
}
