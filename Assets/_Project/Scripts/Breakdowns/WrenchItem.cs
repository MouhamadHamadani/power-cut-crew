using System.Collections;
using PowerCutCrew.Core;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>
    /// The wrench: a grabbable item that is used instead of thrown. On the holder's machine the use button swings it (a short animation of the copy in the hand) and, when the
    /// swing lands on the generator, asks the host to check the hit; the host answers for everybody. A miss knocks the wrench back in the holder's hand. Dropping it (Q) tosses it
    /// forward, so throwing is still possible.
    /// </summary>
    [RequireComponent(typeof(NetworkPhysicsItem))]
    public class WrenchItem : MonoBehaviour, IUsableItem
    {
        private static readonly System.Collections.Generic.List<WrenchItem> _all = new System.Collections.Generic.List<WrenchItem>();

        /// <summary>The wrench of this process (the host's when two network managers live in one process, as in tests).</summary>
        public static WrenchItem Instance
        {
            get
            {
                WrenchItem best = null;
                foreach (WrenchItem w in _all)
                {
                    if (w == null || !w.Item.IsSpawned) { continue; }
                    if (best == null || w.Item.IsServer) { best = w; }
                }
                return best;
            }
        }

        private NetworkPhysicsItem _item;
        private float _nextSwing;
        private Coroutine _animation;

        public NetworkPhysicsItem Item => _item != null ? _item : (_item = GetComponent<NetworkPhysicsItem>());
        public float DropSpeed => BreakdownTuning.Instance.WrenchTossSpeed;

        private void OnEnable()
        {
            _all.Add(this);
            BreakdownVisuals.HitFeedback += OnHitFeedback;
        }

        private void OnDisable()
        {
            _all.Remove(this);
            BreakdownVisuals.HitFeedback -= OnHitFeedback;
        }

        /// <summary>Holder's machine: swing. Returns true (and what was hit) when the swing reached the generator.</summary>
        public bool BeginUse(Transform eye, out Component target, out Vector3 point)
        {
            target = null;
            point = default;
            BreakdownTuning t = BreakdownTuning.Instance;
            if (Time.time < _nextSwing) { return false; }
            _nextSwing = Time.time + t.SwingCooldown;
            Swing(0.28f, -75f);
            bool reached = Physics.Raycast(new Ray(eye.position, eye.forward), out RaycastHit hit, t.HitRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (LaunchArgs.Has("-botlog")) { Debug.Log($"[Wrench] swing: {(reached ? hit.collider.name + " at " + hit.distance.ToString("F2") + " m" : "nothing within " + t.HitRange.ToString("F1") + " m")}"); }
            if (!reached) { return false; }
            if (hit.collider.GetComponentInParent<BreakdownNet>() == null) { return false; }
            target = hit.collider.transform;
            point = hit.point;
            return true;
        }

        public void HostUse(int playerId, Component target, Vector3 point)
        {
            BreakdownNet net = BreakdownNet.Instance;
            if (net != null && net.IsServer) { net.HostWrenchHit(playerId, target, point); }
        }

        // ---------- the holder's copy ----------

        private void OnHitFeedback(HitResult result, Vector3 point)
        {
            // a miss: the wrench bounces back in the holder's hand
            if (!Item.HeldByLocalClient || result == HitResult.Progress || result == HitResult.Fixed) { return; }
            Swing(0.35f, 55f);
        }

        private void Swing(float seconds, float degrees)
        {
            GameObject copy = Item.HeldCopy;
            if (copy == null) { return; }
            if (_animation != null) { StopCoroutine(_animation); }
            _animation = StartCoroutine(SwingRoutine(copy.transform, seconds, degrees));
        }

        private IEnumerator SwingRoutine(Transform copy, float seconds, float degrees)
        {
            Quaternion rest = copy.localRotation;
            Vector3 restPosition = copy.localPosition;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (copy == null) { yield break; }
                float k = Mathf.Sin(t / seconds * Mathf.PI);   // out and back
                copy.localRotation = rest * Quaternion.Euler(degrees * k, 0f, 0f);
                copy.localPosition = restPosition + Vector3.forward * (0.12f * k);
                yield return null;
            }
            if (copy != null) { copy.localRotation = rest; copy.localPosition = restPosition; }
            _animation = null;
        }
    }
}
