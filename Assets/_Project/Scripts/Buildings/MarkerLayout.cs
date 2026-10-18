using UnityEngine;

namespace PowerCutCrew.Buildings
{
    /// <summary>Where a building's marker goes on screen and how it points when the building is out of view.</summary>
    public struct MarkerPlacement
    {
        /// <summary>Screen position in pixels (origin bottom left): the building's anchor while on screen, otherwise the point on the screen edge.</summary>
        public Vector2 Position;
        public bool OnScreen;
        /// <summary>Degrees, 0 = pointing right, 90 = up, -90 = down (counter-clockwise on screen). Only meaningful when off screen.</summary>
        public float ArrowAngle;
    }

    /// <summary>
    /// Placement math for the screen-space building markers (plain C#, unit tested): on-screen test inside the free area of the screen (the HUD strip along the top and
    /// the tip panel along the bottom are reserved), clamping to the edge of that area with the arrow pointing at the building (a building behind the camera lands on the
    /// bottom edge or the nearer side), the distance scale and the upward push that keeps overlapping markers readable.
    /// </summary>
    public static class MarkerLayout
    {
        public static MarkerPlacement Place(Vector2 screenPoint, bool behind, Vector2 screenSize, float margin) => Place(screenPoint, behind, screenSize, margin, margin, margin);

        public static MarkerPlacement Place(Vector2 screenPoint, bool behind, Vector2 screenSize, float margin, float topMargin) => Place(screenPoint, behind, screenSize, margin, topMargin, margin);

        /// <param name="screenPoint">The anchor in pixels, as <c>Camera.WorldToScreenPoint</c> returns it (only x and y are used).</param>
        /// <param name="behind">The anchor is behind the camera (z of the screen point below zero). Unity then projects it mirrored (a point behind and to the right
        /// shows up on the left, one behind and above shows up below the centre), so the sideways direction is flipped back and the vertical one is always down.</param>
        /// <param name="margin">Pixels kept free along the left and right edges.</param>
        /// <param name="topMargin">Pixels kept free along the top edge (the HUD strip).</param>
        /// <param name="bottomMargin">Pixels kept free along the bottom edge (the tip panel and the corner hint).</param>
        public static MarkerPlacement Place(Vector2 screenPoint, bool behind, Vector2 screenSize, float margin, float topMargin, float bottomMargin)
        {
            Vector2 center = screenSize * 0.5f;
            float left = margin, right = Mathf.Max(left + 2f, screenSize.x - margin);
            float bottom = bottomMargin, top = Mathf.Max(bottom + 2f, screenSize.y - topMargin);
            Vector2 p = screenPoint;
            if (!behind && p.x >= left && p.x <= right && p.y >= bottom && p.y <= top)
            {
                Vector2 toPoint = p - center;
                return new MarkerPlacement { Position = p, OnScreen = true, ArrowAngle = Mathf.Atan2(toPoint.y, toPoint.x) * Mathf.Rad2Deg };
            }
            // the free area is not centred on the screen centre: aim from its middle
            var aim = new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f);
            Vector2 d;
            if (behind)
            {
                // behind you: sideways as it really is (the projection is mirrored), and always towards the bottom, where your back is
                d = new Vector2(center.x - screenPoint.x, -Mathf.Abs(center.y - screenPoint.y));
                if (Mathf.Abs(d.x) < 1e-3f && Mathf.Abs(d.y) < 1e-3f) { d = Vector2.down; }
            }
            else
            {
                d = p - aim;
                if (d.sqrMagnitude < 1e-4f) { d = Vector2.down; }
            }
            float tx = d.x > 1e-4f ? (right - aim.x) / d.x : (d.x < -1e-4f ? (left - aim.x) / d.x : float.MaxValue);
            float ty = d.y > 1e-4f ? (top - aim.y) / d.y : (d.y < -1e-4f ? (bottom - aim.y) / d.y : float.MaxValue);
            float t = Mathf.Min(tx, ty);
            return new MarkerPlacement { Position = aim + d * t, OnScreen = false, ArrowAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg };
        }

        /// <summary>1 within <paramref name="near"/>, shrinking linearly to <paramref name="farScale"/> at <paramref name="far"/>, never smaller.</summary>
        public static float Scale(float distance, float near, float far, float farScale)
        {
            if (distance <= near || far <= near) { return 1f; }
            return Mathf.Lerp(1f, farScale, Mathf.Clamp01((distance - near) / (far - near)));
        }

        /// <summary>
        /// Markers are boxes with their bottom centre at <paramref name="positions"/>. Walking from the nearest to the farthest, a marker that overlaps one already placed is
        /// pushed UP to the lowest free spot (a gap of <paramref name="gap"/> pixels above a placed marker) and never below its anchor. When no free spot fits under
        /// <paramref name="ceiling"/> (the HUD strip) it goes as high as the ceiling allows. Positions are changed in place.
        /// </summary>
        public static void Separate(Vector2[] positions, float[] distances, Vector2[] sizes, int count, float gap, float ceiling = float.MaxValue)
        {
            var order = new int[count];
            for (int i = 0; i < count; i++) { order[i] = i; }
            System.Array.Sort(order, (a, b) => distances[a].CompareTo(distances[b]));
            for (int k = 1; k < count; k++)
            {
                int far = order[k];
                float original = positions[far].y;
                if (!Overlaps(positions, sizes, order, k, far, original)) { continue; }
                float best = float.NaN;
                for (int j = 0; j < k; j++)
                {
                    int near = order[j];
                    float above = positions[near].y + sizes[near].y + gap;
                    if (above < original || above + sizes[far].y > ceiling) { continue; }
                    if (Overlaps(positions, sizes, order, k, far, above)) { continue; }
                    if (float.IsNaN(best) || above < best) { best = above; }
                }
                if (float.IsNaN(best))
                {
                    // crowded: as high as the ceiling allows (never below where it was)
                    best = Mathf.Max(original, Mathf.Min(ceiling - sizes[far].y, original + 1000f));
                    if (ceiling >= float.MaxValue * 0.5f) { for (int j = 0; j < k; j++) { best = Mathf.Max(best, positions[order[j]].y + sizes[order[j]].y + gap); } }
                }
                positions[far].y = best;
            }
        }

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
