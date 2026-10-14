using UnityEngine;

namespace PowerCutCrew.Fuel
{
    /// <summary>Plain fuel tank model (jerrycan, generator tank): amount within a capacity. Unit tested.</summary>
    public class FuelContainer
    {
        public float Capacity { get; }
        public float Amount { get; private set; }

        public FuelContainer(float capacity, float amount = 0f)
        {
            Capacity = capacity;
            Amount = Mathf.Clamp(amount, 0f, capacity);
        }

        public bool IsFull => Amount >= Capacity - 0.0001f;
        public bool IsEmpty => Amount <= 0.0001f;
        public float Space => Capacity - Amount;
        public float Fill01 => Capacity > 0f ? Amount / Capacity : 0f;

        public void Set(float amount)
        {
            Amount = Mathf.Clamp(amount, 0f, Capacity);
        }

        /// <summary>Add up to the amount; returns what was accepted.</summary>
        public float Add(float amount)
        {
            float accepted = Mathf.Clamp(amount, 0f, Space);
            Amount += accepted;
            return accepted;
        }

        /// <summary>Take up to the amount; returns what was taken.</summary>
        public float Take(float amount)
        {
            float taken = Mathf.Clamp(amount, 0f, Amount);
            Amount -= taken;
            return taken;
        }

        /// <summary>Move fuel at a rate (units per second) for one step. Returns the amount moved (limited by what the source has and the target has room for).</summary>
        public static float Transfer(FuelContainer from, FuelContainer to, float rate, float deltaTime)
        {
            float wanted = Mathf.Min(rate * deltaTime, from.Amount, to.Space);
            if (wanted <= 0f) { return 0f; }
            from.Take(wanted);
            to.Add(wanted);
            return wanted;
        }
    }
}
