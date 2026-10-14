namespace PowerCutCrew.Cables
{
    public enum CableState : byte
    {
        /// <summary>Coiled by the generator outlet, nobody has touched it yet (a reel is ready).</summary>
        Fresh,
        Held,
        Loose,
        Plugged,
        Snapped,
    }

    public enum CableEvent
    {
        None,
        Snapped,
        PoppedOut,
        ReelReady,
        DespawnPieces,
    }

    public struct CableSettings
    {
        public float SnapStretch;
        public float SnapSeconds;
        public float PopOutStretch;
        public float PopOutSeconds;
        public float ReelReadySeconds;
        public float PiecesDespawnSeconds;

        public static CableSettings Defaults => new CableSettings
        {
            SnapStretch = 0.15f,
            SnapSeconds = 0.5f,
            PopOutStretch = 0.10f,
            PopOutSeconds = 0.5f,
            ReelReadySeconds = 3f,
            PiecesDespawnSeconds = 8f,
        };
    }

    /// <summary>
    /// Cable rules as a small state machine: who has the plug, the tension thresholds (snap while held, pop out while
    /// plugged, each only after the stretch stayed over the limit for a while) and the timers after a snap (fresh cable
    /// on the reel, pieces despawn). Plain C#, run by the host, unit tested.
    /// </summary>
    public class CableModel
    {
        private readonly CableSettings _s;
        private float _overSeconds;
        private float _sinceSnap;
        private bool _reelReadyFired;
        private bool _despawnFired;

        public CableModel(CableSettings settings)
        {
            _s = settings;
        }

        public CableState State { get; private set; } = CableState.Fresh;
        public int SocketId { get; private set; } = -1;

        public bool IsBroken => State == CableState.Snapped;

        public bool Grab()
        {
            if (State != CableState.Fresh && State != CableState.Loose) { return false; }
            State = CableState.Held;
            _overSeconds = 0f;
            return true;
        }

        public bool Release()
        {
            if (State != CableState.Held) { return false; }
            State = CableState.Loose;
            _overSeconds = 0f;
            return true;
        }

        public bool PlugIn(int socketId)
        {
            if (State != CableState.Held) { return false; }
            State = CableState.Plugged;
            SocketId = socketId;
            _overSeconds = 0f;
            return true;
        }

        /// <summary>Unplug into somebody's hands.</summary>
        public bool Unplug()
        {
            if (State != CableState.Plugged) { return false; }
            State = CableState.Held;
            SocketId = -1;
            _overSeconds = 0f;
            return true;
        }

        /// <summary>Advance by a step with the current stretch (0.1 = path 10% longer than the rest length). Returns what happened, if anything.</summary>
        public CableEvent Tick(float deltaTime, float stretch)
        {
            switch (State)
            {
                case CableState.Held:
                    _overSeconds = stretch > _s.SnapStretch ? _overSeconds + deltaTime : 0f;
                    if (_overSeconds >= _s.SnapSeconds)
                    {
                        State = CableState.Snapped;
                        _sinceSnap = 0f;
                        return CableEvent.Snapped;
                    }
                    return CableEvent.None;

                case CableState.Plugged:
                    _overSeconds = stretch > _s.PopOutStretch ? _overSeconds + deltaTime : 0f;
                    if (_overSeconds >= _s.PopOutSeconds)
                    {
                        State = CableState.Loose;
                        SocketId = -1;
                        _overSeconds = 0f;
                        return CableEvent.PoppedOut;
                    }
                    return CableEvent.None;

                case CableState.Snapped:
                    _sinceSnap += deltaTime;
                    if (!_reelReadyFired && _sinceSnap >= _s.ReelReadySeconds)
                    {
                        _reelReadyFired = true;
                        return CableEvent.ReelReady;
                    }
                    if (!_despawnFired && _sinceSnap >= _s.PiecesDespawnSeconds)
                    {
                        _despawnFired = true;
                        return CableEvent.DespawnPieces;
                    }
                    return CableEvent.None;

                default:
                    return CableEvent.None;
            }
        }
    }

    public enum PlugInResult
    {
        Ok,
        NotHoldingPlug,
        NoTrustedPose,
        TooFar,
        SocketOccupied,
        CableBroken,
    }

    /// <summary>Host-side validation of a plug-in request. Plain C#, unit tested.</summary>
    public static class PlugRules
    {
        public static PlugInResult Validate(bool holdsThisPlug, bool hasTrustedPose, float poseToSocketDistance, float plugReach, bool socketFree, bool cableIntact)
        {
            if (!holdsThisPlug) { return PlugInResult.NotHoldingPlug; }
            if (!cableIntact) { return PlugInResult.CableBroken; }
            if (!hasTrustedPose) { return PlugInResult.NoTrustedPose; }
            if (poseToSocketDistance > plugReach) { return PlugInResult.TooFar; }
            if (!socketFree) { return PlugInResult.SocketOccupied; }
            return PlugInResult.Ok;
        }
    }
}
