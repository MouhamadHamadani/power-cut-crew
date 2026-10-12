using System.Collections.Generic;

namespace PowerCutCrew.Core
{
    /// <summary>
    /// Host-side arbitration for exclusive interactions (grabbing). Requests are processed one at a time, so when two
    /// players ask for the same target in the same tick the first one processed wins and the other gets nothing.
    /// Plain C#, unit tested.
    /// </summary>
    public class InteractionArbiter
    {
        private readonly Dictionary<ulong, int> _holderByTarget = new Dictionary<ulong, int>();

        /// <summary>Claim a target. False when somebody else already holds it or the player already holds it.</summary>
        public bool TryAcquire(ulong targetId, int playerId)
        {
            if (_holderByTarget.ContainsKey(targetId))
            {
                return false;
            }
            _holderByTarget[targetId] = playerId;
            return true;
        }

        /// <summary>Release a target, but only if this player is the holder.</summary>
        public bool Release(ulong targetId, int playerId)
        {
            if (_holderByTarget.TryGetValue(targetId, out int holder) && holder == playerId)
            {
                _holderByTarget.Remove(targetId);
                return true;
            }
            return false;
        }

        /// <summary>Release everything a player holds (disconnect). Returns the released target ids.</summary>
        public List<ulong> ReleaseAll(int playerId)
        {
            var released = new List<ulong>();
            foreach (KeyValuePair<ulong, int> pair in _holderByTarget)
            {
                if (pair.Value == playerId)
                {
                    released.Add(pair.Key);
                }
            }
            foreach (ulong id in released)
            {
                _holderByTarget.Remove(id);
            }
            return released;
        }

        public void Clear() => _holderByTarget.Clear();

        public bool IsHeld(ulong targetId) => _holderByTarget.ContainsKey(targetId);

        public int HolderOf(ulong targetId) => _holderByTarget.TryGetValue(targetId, out int h) ? h : -1;

        public bool HoldsAnything(int playerId)
        {
            foreach (int holder in _holderByTarget.Values)
            {
                if (holder == playerId)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
