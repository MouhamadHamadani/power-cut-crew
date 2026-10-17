using System;

namespace PowerCutCrew.Buildings
{
    public enum DemandWindow : byte { Quiet = 0, Requesting = 1 }

    /// <summary>What happened to a building during one tick (for neighbor lines and metrics).</summary>
    [Flags]
    public enum BuildingEvent
    {
        None = 0,
        RequestStarted = 1,
        UnpoweredLong = 2,
        SpikeStarted = 4,
        BecameAngry = 8,
        RequestEnded = 16,
    }

    /// <summary>The numbers a <see cref="BuildingModel"/> needs (filled from <c>BuildingTuning</c>).</summary>
    public struct BuildingSettings
    {
        public float RequestMin, RequestMax;
        public float QuietMin, QuietMax;
        public float InitialDelayMin, InitialDelayMax;
        public float SpikeChance, SpikeMultiplier, SpikeSeconds;
        public float StartHappiness, HappinessRise, HappinessFall;
        public float RatePerAmpPerMinute;
        public float UnpoweredLineSeconds;

        public static BuildingSettings Defaults => new BuildingSettings
        {
            RequestMin = 60f, RequestMax = 120f,
            QuietMin = 30f, QuietMax = 60f,
            InitialDelayMin = 3f, InitialDelayMax = 25f,
            SpikeChance = 0.2f, SpikeMultiplier = 1.5f, SpikeSeconds = 20f,
            StartHappiness = 70f, HappinessRise = 2f, HappinessFall = 3f,
            RatePerAmpPerMinute = 1f,
            UnpoweredLineSeconds = 10f,
        };
    }

    /// <summary>
    /// One building's demand, happiness and payment (plain C#, host only, unit tested). It alternates between Requesting and Quiet windows
    /// drawn from a seeded random generator, so the same seed gives the same schedule. While Requesting and powered it draws its
    /// subscription (1.5 x during a peak spike), pays by the amp and gains happiness; while Requesting and unpowered it loses happiness
    /// and, at 0, turns Angry for the rest of the shift (no more payment).
    /// </summary>
    public class BuildingModel
    {
        private readonly BuildingSettings _s;
        private readonly int _seed;
        private Random _rng;

        private bool _running;
        private float _windowLeft;
        private float _windowElapsed;
        private float _spikeAt;
        private bool _spikeDone;
        private float _spikeLeft;
        private float _unpoweredFor;
        private bool _unpoweredReported;

        public BuildingModel(BuildingSettings settings, float subscriptionAmps, int seed)
        {
            _s = settings;
            Subscription = subscriptionAmps;
            _seed = seed;
            Stop();
        }

        /// <summary>Amps this building has subscribed to (and pays for).</summary>
        public float Subscription { get; }

        public DemandWindow Window { get; private set; }
        public bool Spiking { get; private set; }
        public float Happiness { get; private set; }
        public bool Angry { get; private set; }
        public bool Running => _running;
        public float WindowLeft => _windowLeft;

        public bool Requesting => _running && Window == DemandWindow.Requesting;

        /// <summary>Amps it draws right now when powered: nothing while Quiet, the subscription while Requesting, 1.5 x during a spike.</summary>
        public float Amps => Requesting ? Subscription * (Spiking ? _s.SpikeMultiplier : 1f) : 0f;

        /// <summary>Seconds spent Requesting, and of those seconds how many were powered (the uptime numbers).</summary>
        public float RequestingSeconds { get; private set; }
        public float PoweredRequestingSeconds { get; private set; }
        public float Earned { get; private set; }
        public int Spikes { get; private set; }

        /// <summary>Back to the lobby state: nothing requests, nothing is drawn.</summary>
        public void Stop()
        {
            _running = false;
            Window = DemandWindow.Quiet;
            Spiking = false;
            Angry = false;
            Happiness = _s.StartHappiness;
            _windowLeft = 0f;
            _windowElapsed = 0f;
            _spikeAt = -1f;
            _spikeDone = false;
            _spikeLeft = 0f;
            _unpoweredFor = 0f;
            _unpoweredReported = false;
            RequestingSeconds = 0f;
            PoweredRequestingSeconds = 0f;
            Earned = 0f;
            Spikes = 0;
        }

        /// <summary>The shift begins: a fresh, seeded schedule that starts with a short Quiet delay so buildings do not all request at once.</summary>
        public void Start()
        {
            Stop();
            _rng = new Random(_seed);
            _running = true;
            Window = DemandWindow.Quiet;
            _windowLeft = Lerp(_s.InitialDelayMin, _s.InitialDelayMax, (float)_rng.NextDouble());
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private void BeginRequest()
        {
            Window = DemandWindow.Requesting;
            _windowLeft = Lerp(_s.RequestMin, _s.RequestMax, (float)_rng.NextDouble());
            _windowElapsed = 0f;
            _spikeDone = false;
            _spikeLeft = 0f;
            Spiking = false;
            _unpoweredFor = 0f;
            _unpoweredReported = false;
            // the spike (the ACs come on) can start anywhere in the window as long as it fits
            bool spikes = _rng.NextDouble() < _s.SpikeChance;
            double where = _rng.NextDouble();
            _spikeAt = spikes ? (float)(where * Math.Max(0f, _windowLeft - _s.SpikeSeconds)) : -1f;
        }

        /// <summary>
        /// Advance by dt. <paramref name="powered"/> is whether the building's socket has power right now; <paramref name="allowStart"/> says whether another
        /// building may start requesting (the group allows at most a few at once; a Quiet window that is over simply waits).
        /// </summary>
        public BuildingEvent Tick(float dt, bool powered, bool allowStart, out float earned)
        {
            earned = 0f;
            BuildingEvent events = BuildingEvent.None;
            if (!_running) { return events; }
            _windowLeft -= dt;

            if (Window == DemandWindow.Requesting)
            {
                RequestingSeconds += dt;
                if (powered) { PoweredRequestingSeconds += dt; }

                if (!Angry)
                {
                    Happiness = Math.Max(0f, Math.Min(100f, Happiness + (powered ? _s.HappinessRise : -_s.HappinessFall) * dt));
                    if (Happiness <= 0f)
                    {
                        Angry = true;
                        events |= BuildingEvent.BecameAngry;
                    }
                }
                if (powered && !Angry)
                {
                    earned = _s.RatePerAmpPerMinute * Subscription / 60f * dt;
                    Earned += earned;
                }

                if (powered) { _unpoweredFor = 0f; _unpoweredReported = false; }
                else
                {
                    _unpoweredFor += dt;
                    if (!_unpoweredReported && _unpoweredFor >= _s.UnpoweredLineSeconds)
                    {
                        _unpoweredReported = true;
                        events |= BuildingEvent.UnpoweredLong;
                    }
                }

                _windowElapsed += dt;
                if (_spikeAt >= 0f && !_spikeDone && _windowElapsed >= _spikeAt)
                {
                    _spikeDone = true;
                    Spiking = true;
                    _spikeLeft = _s.SpikeSeconds;
                    Spikes++;
                    events |= BuildingEvent.SpikeStarted;
                }
                if (Spiking)
                {
                    _spikeLeft -= dt;
                    if (_spikeLeft <= 0f) { Spiking = false; }
                }

                if (_windowLeft <= 0f)
                {
                    Window = DemandWindow.Quiet;
                    Spiking = false;
                    _windowLeft = Lerp(_s.QuietMin, _s.QuietMax, (float)_rng.NextDouble());
                    events |= BuildingEvent.RequestEnded;
                }
            }
            else if (_windowLeft <= 0f)
            {
                if (allowStart)
                {
                    BeginRequest();
                    events |= BuildingEvent.RequestStarted;
                }
                else
                {
                    _windowLeft = 0f;   // wait for a free slot
                }
            }
            return events;
        }
    }
}
