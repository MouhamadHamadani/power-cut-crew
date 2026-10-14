using UnityEngine;

namespace PowerCutCrew.Generator
{
    public enum GeneratorState : byte
    {
        Off,
        Running,
        OutOfFuel,
        Overheated,
    }

    public enum PullResult
    {
        Started,
        Failed,
        NoFuel,
        Locked,
        AlreadyRunning,
    }

    /// <summary>All generator numbers as plain values (built from the GeneratorTuning asset, or by tests).</summary>
    public struct GeneratorSettings
    {
        public float Capacity;
        public float IdleBurn;
        public float PerAmpBurn;
        public float RatedAmps;
        public float HeatPerAmpOver;
        public float CoolRunning;
        public float CoolOff;
        public float StallHeat;
        public float RestartBelowHeat;
        public float StartChanceCold;
        public float StartChancePerFail;
        public int GuaranteedAtPull;

        public static GeneratorSettings Defaults => new GeneratorSettings
        {
            Capacity = 100f,
            IdleBurn = 0.2f,
            PerAmpBurn = 0.05f,
            RatedAmps = 10f,
            HeatPerAmpOver = 1.5f,
            CoolRunning = 2f,
            CoolOff = 1f,
            StallHeat = 100f,
            RestartBelowHeat = 60f,
            StartChanceCold = 0.35f,
            StartChancePerFail = 0.25f,
            GuaranteedAtPull = 4,
        };
    }

    /// <summary>
    /// The generator rules: pull-cord start odds with a guarantee, fuel burn, load, heat and the overheat lockout.
    /// Plain C#, run by the host only (GeneratorNet), unit tested. Randomness is injected as a roll in [0, 1).
    /// </summary>
    public class GeneratorModel
    {
        private readonly GeneratorSettings _s;

        public GeneratorModel(GeneratorSettings settings, float fuel = 0f)
        {
            _s = settings;
            Fuel = Mathf.Clamp(fuel, 0f, settings.Capacity);
            State = GeneratorState.Off;
        }

        public GeneratorState State { get; private set; }
        public float Fuel { get; private set; }
        public float Heat { get; private set; }

        /// <summary>Amps drawn right now (zero unless running).</summary>
        public float LoadAmps { get; private set; }

        public int FailedPulls { get; private set; }
        public float Capacity => _s.Capacity;
        public float RatedAmps => _s.RatedAmps;
        public float TankSpace => _s.Capacity - Fuel;

        /// <summary>Chance that the next pull starts the engine.</summary>
        public float NextStartChance
        {
            get
            {
                if (FailedPulls + 1 >= _s.GuaranteedAtPull) { return 1f; }
                return Mathf.Min(1f, _s.StartChanceCold + FailedPulls * _s.StartChancePerFail);
            }
        }

        /// <summary>Pull the cord. The roll decides whether it starts (roll below the chance).</summary>
        public PullResult Pull(double roll)
        {
            if (State == GeneratorState.Running) { return PullResult.AlreadyRunning; }
            if (State == GeneratorState.Overheated) { return PullResult.Locked; }
            if (Fuel <= 0f) { return PullResult.NoFuel; }
            if (roll < NextStartChance)
            {
                State = GeneratorState.Running;
                FailedPulls = 0;
                return PullResult.Started;
            }
            FailedPulls++;
            return PullResult.Failed;
        }

        public bool Stop()
        {
            if (State != GeneratorState.Running) { return false; }
            State = GeneratorState.Off;
            LoadAmps = 0f;
            return true;
        }

        /// <summary>Pour fuel into the tank; returns what was accepted. An empty generator becomes Off (it needs a pull to restart).</summary>
        public float AddFuel(float amount)
        {
            float accepted = Mathf.Clamp(amount, 0f, TankSpace);
            Fuel += accepted;
            if (State == GeneratorState.OutOfFuel && Fuel > 0f) { State = GeneratorState.Off; }
            return accepted;
        }

        /// <summary>Advance by one step with the amps the powered sockets ask for.</summary>
        public void Tick(float deltaTime, float requestedLoadAmps)
        {
            if (State == GeneratorState.Running)
            {
                LoadAmps = requestedLoadAmps;
                Fuel -= (_s.IdleBurn + _s.PerAmpBurn * LoadAmps) * deltaTime;
                if (LoadAmps > _s.RatedAmps) { Heat += (LoadAmps - _s.RatedAmps) * _s.HeatPerAmpOver * deltaTime; }
                else { Heat -= _s.CoolRunning * deltaTime; }
                Heat = Mathf.Clamp(Heat, 0f, 100f);
                if (Fuel <= 0f)
                {
                    Fuel = 0f;
                    State = GeneratorState.OutOfFuel;
                    LoadAmps = 0f;
                }
                else if (Heat >= _s.StallHeat)
                {
                    State = GeneratorState.Overheated;
                    LoadAmps = 0f;
                }
                return;
            }

            LoadAmps = 0f;
            Heat = Mathf.Max(0f, Heat - _s.CoolOff * deltaTime);
            if (State == GeneratorState.Overheated && Heat < _s.RestartBelowHeat) { State = GeneratorState.Off; }
        }
    }
}
