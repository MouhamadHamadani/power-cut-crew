using UnityEngine;

namespace PowerCutCrew.Buildings
{
    /// <summary>Where a building's marker goes on screen and how it points when the building is out of view.</summary>
    public struct MarkerPlacement
    {
        /// <summary>Screen position in pixels (origin bottom left): the building's anchor while on screen, otherwise the point on the screen edge.</summary>
        public Vector2 Position;
        public bool OnScreen;
        /// <summary>Degrees, 0 = pointing right, 90 = up (counter-clockwise on screen). Only meaningful when off screen.</summary>
        public float ArrowAngle;
    }

    /// <summary>
    /// Placement math for the screen-space building markers (plain C#, unit tested): on-screen test, clamping to the screen edge with the
    /// arrow pointing at the building (also for buildings behind the camera), the distance scale and the vertical push that keeps two
    /// overlapping markers readable.
    /// </summary>
    public static class MarkerLayout
    {
        /// <param name="screenPoint">The anchor in pixels, as <c>Camera.WorldToScreenPoint</c> returns it (only x and y are used).</param>
        /// <param name="behind">The anchor is behind the camera (z of the screen point below zero): its x and y are mirrored then.</param>
        /// <param name="margin">Pixels kept free along every screen edge.</param>
        public static MarkerPlacement Place(Vector2 screenPoint, bool behind, Vector2 screenSize, float margin) => Place(screenPoint, behind, screenSize, margin, margin);

        /// <param name="topMargin">Pixels kept free along the top edge (the HUD strip): bigger than <paramref name="margin"/> keeps markers under it.</param>
        public static MarkerPlacement Place(Vector2 screenPoint, bool behind, Vector2 screenSize, float margin, float topMargin)
        {
            Vector2 center = screenSize * 0.5f;
            float left = margin, right = Mathf.Max(left + 2f, screenSize.x - margin);
            float bottom = margin, top = Mathf.Max(bottom + 2f, screenSize.y - topMargin);
            Vector2 p = behind ? 2f * center - screenPoint : screenPoint;   // a point behind the camera projects mirrored through the centre
            Vector2 d = p - center;
            if (!behind && p.x >= left && p.x <= right && p.y >= bottom && p.y <= top)
            {
                return new MarkerPlacement { Position = screenPoint, OnScreen = true, ArrowAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg };
            }
            if (d.sqrMagnitude < 1e-4f) { d = Vector2.down; }   // exactly behind the middle of the view: point down
            float tx = d.x > 1e-4f ? (right - center.x) / d.x : (d.x < -1e-4f ? (left - center.x) / d.x : float.MaxValue);
            float ty = d.y > 1e-4f ? (top - center.y) / d.y : (d.y < -1e-4f ? (bottom - center.y) / d.y : float.MaxValue);
            float t = Mathf.Min(tx, ty);
            return new MarkerPlacement { Position = center + d * t, OnScreen = false, ArrowAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg };
        }

        /// <summary>1 within <paramref name="near"/>, shrinking linearly to <paramref name="farScale"/> at <paramref name="far"/>, never smaller.</summary>
        public static float Scale(float distance, float near, float far, float farScale)
        {
            if (distance <= near || far <= near) { return 1f; }
            return Mathf.Lerp(1f, farScale, Mathf.Clamp01((distance - near) / (far - near)));
        }

        /// <summary>
        /// Markers are boxes with their bottom centre at <paramref name="positions"/>. Walking from the nearest to the farthest, a marker that overlaps a nearer one
        /// moves to the closest free spot directly above or below any marker already placed (a gap of <paramref name="gap"/> pixels), staying under
        /// <paramref name="ceiling"/> (the HUD strip) and above the bottom of the screen. Positions are changed in place.
        /// </summary>
        public static void Separate(Vector2[] positions, float[] distances, Vector2[] sizes, int count, float gap, float ceiling = float.MaxValue)
        {
            var order = new int[count];
            for (int i = 0; i < count; i++) { order[i] = i; }
            System.Array.Sort(order, (a, b) => distances[a].CompareTo(distances[b]));
            for (int k = 1; k < count; k++)
            {
                int far = order[k];
                if (!Overlaps(positions, sizes, order, k, far, positions[far].y)) { continue; }
                float original = positions[far].y;
                float best = float.NaN;
                for (int j = 0; j < k; j++)
                {
                    int near = order[j];
                    float above = positions[near].y + sizes[near].y + gap;
                    float below = positions[near].y - sizes[far].y - gap;
                    best = Better(best, above, original, positions, sizes, order, k, far, ceiling);
                    best = Better(best, below, original, positions, sizes, order, k, far, ceiling);
                }
                if (float.IsNaN(best))
                {
                    // no free spot (a very crowded screen): stack above the highest marker placed so far
                    best = original;
                    for (int j = 0; j < k; j++) { best = Mathf.Max(best, positions[order[j]].y + sizes[order[j]].y + gap); }
                }
                positions[far].y = best;
            }
        }

        private static float Better(float best, float candidate, float original, Vector2[] positions, Vector2[] sizes, int[] order, int placed, int far, float ceiling)
        {
            if (candidate < 0f || candidate + sizes[far].y > ceiling) { return best; }
            if (Overlaps(positions, sizes, order, placed, far, candidate)) { return best; }
            return float.IsNaN(best) || Mathf.Abs(candidate - original) < Mathf.Abs(best - original) ? candidate : best;
        }

        /// <summary>Would marker <paramref name="far"/> at height <paramref name="y"/> overlap one of the first <paramref name="placed"/> markers of the order?</summary>
        private static bool Overlaps(Vector2[] positions, Vector2[] sizes, int[] order, int placed, int far, float y)
        {
            for (int j = 0; j < placed; j++)
            {
                int near = order[j];
                bool overlapX = Mathf.Abs(positions[far].x - positions[near].x) < (sizes[far].x + sizes[near].x) * 0.5f;
                bool overlapY = y < positions[near].y + sizes[near].y && y + sizes[far].y > positions[near].y;
                if (overlapX && overlapY) { return true; }
            }
            return false;
        }
    }
}
