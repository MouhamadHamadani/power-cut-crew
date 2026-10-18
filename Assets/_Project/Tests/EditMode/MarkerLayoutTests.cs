using NUnit.Framework;
using PowerCutCrew.Buildings;
using UnityEngine;

namespace PowerCutCrew.Tests.EditMode
{
    public class MarkerLayoutTests
    {
        private static readonly Vector2 Screen = new Vector2(1920f, 1080f);

        [Test]
        public void AnAnchorInsideTheMarginStaysWhereItIs()
        {
            MarkerPlacement p = MarkerLayout.Place(new Vector2(800f, 600f), false, Screen, 60f);
            Assert.IsTrue(p.OnScreen);
            Assert.AreEqual(new Vector2(800f, 600f), p.Position);
        }

        [Test]
        public void AnAnchorOffTheRightClampsToTheRightEdge_ArrowPointsRight()
        {
            MarkerPlacement p = MarkerLayout.Place(new Vector2(3000f, 540f), false, Screen, 60f);
            Assert.IsFalse(p.OnScreen);
            Assert.AreEqual(1920f - 60f, p.Position.x, 1e-3f);
            Assert.AreEqual(540f, p.Position.y, 1e-3f);
            Assert.AreEqual(0f, p.ArrowAngle, 1e-3f);
        }

        [Test]
        public void AnAnchorAboveTheScreenClampsToTheTopEdge_ArrowPointsUp()
        {
            MarkerPlacement p = MarkerLayout.Place(new Vector2(960f, 2000f), false, Screen, 60f);
            Assert.IsFalse(p.OnScreen);
            Assert.AreEqual(1080f - 60f, p.Position.y, 1e-3f);
            Assert.AreEqual(90f, p.ArrowAngle, 1e-3f);
        }

        [Test]
        public void ADiagonalAnchorLandsOnTheEdgeAlongTheRayFromTheCentre()
        {
            MarkerPlacement p = MarkerLayout.Place(new Vector2(960f + 4000f, 540f + 4000f), false, Screen, 60f);
            Assert.IsFalse(p.OnScreen);
            Assert.That(p.Position.x, Is.InRange(60f, 1860f));
            Assert.That(p.Position.y, Is.InRange(60f, 1020f));
            Assert.IsTrue(Mathf.Approximately(p.Position.x, 1860f) || Mathf.Approximately(p.Position.y, 1020f), "on the border");
            Assert.AreEqual(45f, p.ArrowAngle, 1e-3f);
        }

        [Test]
        public void BehindTheCameraIsAlwaysOffScreen_AndTheMirroredPointGivesTheDirection()
        {
            // WorldToScreenPoint of a point behind the camera and to the right shows it on the left; the arrow must point right
            MarkerPlacement p = MarkerLayout.Place(new Vector2(960f - 500f, 540f), true, Screen, 60f);
            Assert.IsFalse(p.OnScreen);
            Assert.AreEqual(1860f, p.Position.x, 1e-3f);
            Assert.AreEqual(0f, p.ArrowAngle, 1e-3f);
            // even when its mirrored projection falls inside the screen
            Assert.IsFalse(MarkerLayout.Place(new Vector2(960f, 540f), true, Screen, 60f).OnScreen);
        }

        [Test]
        public void AMoreGenerousTopMarginKeepsMarkersUnderTheHud()
        {
            Assert.IsTrue(MarkerLayout.Place(new Vector2(960f, 1000f), false, Screen, 60f).OnScreen);
            MarkerPlacement p = MarkerLayout.Place(new Vector2(960f, 1000f), false, Screen, 60f, 140f);
            Assert.IsFalse(p.OnScreen, "inside the reserved strip counts as off screen");
            Assert.AreEqual(1080f - 140f, p.Position.y, 1e-3f);
            Assert.AreEqual(90f, p.ArrowAngle, 1e-3f);
            Assert.IsTrue(MarkerLayout.Place(new Vector2(960f, 900f), false, Screen, 60f, 140f).OnScreen);
        }

        [Test]
        public void ScaleIsFullNear_ShrinksToTheFarScale_AndNeverBelowIt()
        {
            Assert.AreEqual(1f, MarkerLayout.Scale(5f, 14f, 50f, 0.6f));
            Assert.AreEqual(1f, MarkerLayout.Scale(14f, 14f, 50f, 0.6f));
            Assert.AreEqual(0.8f, MarkerLayout.Scale(32f, 14f, 50f, 0.6f), 1e-4f);
            Assert.AreEqual(0.6f, MarkerLayout.Scale(50f, 14f, 50f, 0.6f), 1e-4f);
            Assert.AreEqual(0.6f, MarkerLayout.Scale(400f, 14f, 50f, 0.6f), 1e-4f);
        }

        [Test]
        public void OverlappingMarkersMoveTheFartherOneToTheClosestFreeSpot_AndLeaveSeparateOnesAlone()
        {
            var sizes = new[] { new Vector2(300f, 150f), new Vector2(300f, 150f), new Vector2(300f, 150f) };
            var pos = new[] { new Vector2(500f, 400f), new Vector2(520f, 420f), new Vector2(1200f, 400f) };
            var dist = new[] { 30f, 20f, 25f };   // marker 1 is the nearest of the overlapping pair
            MarkerLayout.Separate(pos, dist, sizes, 3, 8f);
            Assert.AreEqual(new Vector2(520f, 420f), pos[1], "the nearer one stays");
            Assert.AreEqual(420f - 150f - 8f, pos[0].y, 1e-3f, "the farther one sits right below it (closer to where it was than above it)");
            Assert.AreEqual(500f, pos[0].x);
            Assert.AreEqual(new Vector2(1200f, 400f), pos[2], "a marker that overlaps nobody is not moved");
        }

        [Test]
        public void ANearCeilingPushesTheFartherMarkerBelowInsteadOfIntoTheHud()
        {
            var sizes = new[] { new Vector2(300f, 150f), new Vector2(300f, 150f) };
            var pos = new[] { new Vector2(500f, 800f), new Vector2(500f, 800f) };
            var dist = new[] { 10f, 20f };
            MarkerLayout.Separate(pos, dist, sizes, 2, 8f, 980f);   // 800 + 150 + 8 + 150 would reach 1108, above the ceiling
            Assert.AreEqual(800f, pos[0].y);
            Assert.AreEqual(800f - 150f - 8f, pos[1].y, 1e-3f);
        }

        [Test]
        public void FourMarkersAreAllPlacedWithoutOverlap_EvenUnderACeiling()
        {
            var sizes = new Vector2[4];
            var pos = new Vector2[4];
            var dist = new[] { 10f, 20f, 30f, 40f };
            for (int i = 0; i < 4; i++) { sizes[i] = new Vector2(300f, 150f); pos[i] = new Vector2(500f + i * 40f, 700f); }
            MarkerLayout.Separate(pos, dist, sizes, 4, 8f, 900f);
            for (int a = 0; a < 4; a++)
            {
                Assert.That(pos[a].y, Is.GreaterThanOrEqualTo(0f));
                Assert.That(pos[a].y + sizes[a].y, Is.LessThanOrEqualTo(900f + 1e-3f), $"marker {a} stays under the ceiling");
                for (int c = a + 1; c < 4; c++)
                {
                    bool overlap = Mathf.Abs(pos[a].x - pos[c].x) < 300f && pos[a].y < pos[c].y + 150f && pos[a].y + 150f > pos[c].y;
                    Assert.IsFalse(overlap, $"markers {a} and {c} overlap");
                }
            }
        }

        [Test]
        public void ThreeMarkersInARowStackWithoutOverlap()
        {
            var sizes = new[] { new Vector2(300f, 150f), new Vector2(300f, 150f), new Vector2(300f, 150f) };
            var pos = new[] { new Vector2(500f, 400f), new Vector2(500f, 400f), new Vector2(500f, 400f) };
            var dist = new[] { 10f, 20f, 30f };
            MarkerLayout.Separate(pos, dist, sizes, 3, 0f);
            Assert.AreEqual(400f, pos[0].y);
            Assert.AreEqual(550f, pos[1].y, 1e-3f, "above the nearest (a tie with below: above wins)");
            Assert.AreEqual(250f, pos[2].y, 1e-3f, "below the nearest is the closest free spot");
        }
    }
}
