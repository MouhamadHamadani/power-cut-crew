using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// Host-side rule for releases (drop, throw, plug-in) that carry the holder's local hand pose: the pose is trusted
    /// only when it is within a tolerance of where the host knows the holder is. Plain C#, unit tested.
    /// </summary>
    public static class ReleasePoseRule
    {
        /// <summary>True when the claimed hand position is within the tolerance of the holder's position as the host knows it.</summary>
        public static bool Accept(Vector3 claimedHandPosition, Vector3 hostHolderPosition, float tolerance)
        {
            if (float.IsNaN(claimedHandPosition.x) || float.IsNaN(claimedHandPosition.y) || float.IsNaN(claimedHandPosition.z)) { return false; }
            return Vector3.Distance(claimedHandPosition, hostHolderPosition) <= tolerance;
        }

        /// <summary>Clamp a claimed throw velocity so a modified client cannot launch items faster than the throw speed (plus a margin).</summary>
        public static Vector3 ClampVelocity(Vector3 claimed, float maxSpeed)
        {
            if (float.IsNaN(claimed.x) || float.IsNaN(claimed.y) || float.IsNaN(claimed.z)) { return Vector3.zero; }
            return Vector3.ClampMagnitude(claimed, maxSpeed);
        }
    }
}
