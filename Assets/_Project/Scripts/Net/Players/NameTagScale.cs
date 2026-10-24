using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// Keeps another player's name tag a readable size on screen (plain C#, unit tested). A tag of world scale 1 looks <c>referenceDistance / distance</c> times its
    /// size at the reference distance; that factor is clamped to [min, max] and the world scale that gives it is returned. So a tag never gets huge next to the camera
    /// and never shrinks to nothing far away.
    /// </summary>
    public static class NameTagScale
    {
        public static float For(float distance, float referenceDistance, float minScreenFactor, float maxScreenFactor)
        {
            float d = Mathf.Max(0.01f, distance);
            float natural = referenceDistance / d;
            float shown = Mathf.Clamp(natural, minScreenFactor, maxScreenFactor);
            return shown * d / referenceDistance;
        }
    }
}
