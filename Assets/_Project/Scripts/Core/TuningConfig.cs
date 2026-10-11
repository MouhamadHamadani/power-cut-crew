using UnityEngine;

namespace PowerCutCrew.Core
{
    /// <summary>
    /// Base class for every tuning asset. All tunable numbers live in config assets, never in code.
    /// Pattern:
    /// <code>
    /// [CreateAssetMenu(menuName = "Power Cut Crew/Config/Generator", fileName = "GeneratorConfig")]
    /// public class GeneratorConfig : TuningConfig
    /// {
    ///     public float MaxAmps = 30f;
    /// }
    /// </code>
    /// </summary>
    public abstract class TuningConfig : ScriptableObject
    {
    }
}
