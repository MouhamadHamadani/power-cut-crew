using System;
using PowerCutCrew.Generator;
using UnityEngine;

namespace PowerCutCrew.Breakdowns
{
    /// <summary>
    /// What the breakdowns look like, on every machine, drawn from <see cref="BreakdownNet"/>'s NetworkVariables (nothing is sent per frame): the generator coughs black smoke
    /// and shakes, the belt side puffs grey smoke, the breaker lever hangs down and the tripped outlet's lamp is red; the sweet spot glows lamp yellow, the belt slot and the
    /// lever pulse while they are what needs doing, and a thin bar fills while somebody holds a fix. One-shot effects (sparks, a clang, the old belt flying off) come in as
    /// calls from the net layer's RPCs.
    /// </summary>
    public class BreakdownVisuals : MonoBehaviour
    {
        public static BreakdownVisuals Instance { get; private set; }

        /// <summary>Every machine: a wrench hit was answered (the holder's wrench bounces on a miss).</summary>
        public static event Action<HitResult, Vector3> HitFeedback;

        public static readonly Color Lamp = new Color32(0xFF, 0xC9, 0x3C, 0xFF);   // lamp yellow #FFC93C
        public static readonly Color Spark = new Color32(0xFF, 0x4D, 0x3D, 0xFF);  // spark red #FF4D3D

        [Header("Parts")]
        [Tooltip("The housing: it shakes (the root keeps still, the panel and the outlets stay readable).")]
        [SerializeField] private Transform _body;
        [SerializeField] private ParticleSystem _blackSmoke;
        [SerializeField] private ParticleSystem _greySmoke;
        [SerializeField] private ParticleSystem _sparks;
        [SerializeField] private Renderer[] _sweetSpotGlows;
        [SerializeField] private Renderer _beltSlotGlow;
        [SerializeField] private Renderer _leverKnob;
        [SerializeField] private Transform _leverPivot;
        [SerializeField] private Renderer[] _outletLamps;
        [SerializeField] private Transform _beltBar;
        [SerializeField] private Transform _resetBar;
        [SerializeField] private Material _oldBeltMaterial;

        [Header("Look")]
        [SerializeField] private float _shakeDegrees = 2.5f;
        [SerializeField] private float _coughSeconds = 1.25f;
        [Tooltip("The lever stands up when all is well and flops over sideways (a turn about the z axis) when the breaker tripped.")]
        [SerializeField] private float _leverUpDegrees;
        [SerializeField] private float _leverDownDegrees = 80f;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock _block;
        private float _lever;
        private float _flash;
        private Vector3 _bodyScale = Vector3.one;
        private Quaternion _bodyRotation = Quaternion.identity;
        private float _hitFlash;
        private int _hitFlashSpot = -1;

        public float ShakeDegrees { get; private set; }

        private void Awake()
        {
            if (Instance == null) { Instance = this; }
            _block = new MaterialPropertyBlock();
            if (_body != null) { _bodyRotation = _body.localRotation; }
            _lever = _leverUpDegrees;
            if (_leverPivot != null) { _leverPivot.localRotation = Quaternion.Euler(0f, 0f, _lever); }
        }

        private void OnDestroy()
        {
            if (Instance == this) { Instance = null; }
        }

        // ---------- one-shot effects ----------

        public void PlayStart(BreakdownType type)
        {
            if (type == BreakdownType.Breaker) { Burst(_sparks, _leverKnob != null ? _leverKnob.transform.position : transform.position, 24); }
            if (type == BreakdownType.Sputter) { Burst(_blackSmoke, _blackSmoke != null ? _blackSmoke.transform.position : transform.position, 14); }
            if (type == BreakdownType.Belt) { Burst(_greySmoke, _greySmoke != null ? _greySmoke.transform.position : transform.position, 14); }
        }

        public void PlayHit(HitResult result, Vector3 point)
        {
            if (result == HitResult.Progress || result == HitResult.Fixed)
            {
                Burst(_sparks, point, result == HitResult.Fixed ? 30 : 12);
                _hitFlash = 0.35f;
                _hitFlashSpot = BreakdownNet.Instance != null ? BreakdownNet.Instance.SweetSpotIndex : -1;
            }
            else
            {
                Burst(_sparks, point, 4);   // a clang: a couple of sparks
            }
            HitFeedback?.Invoke(result, point);
        }

        public void PlayFixed(BreakdownType type)
        {
            _flash = 0.5f;
            if (type == BreakdownType.Belt) { Burst(_greySmoke, _greySmoke != null ? _greySmoke.transform.position : transform.position, 6); }
        }

        /// <summary>The old belt flies out of its slot as a small local prop: cheaper than a network object for something nobody can pick up and that is gone in 20 s.</summary>
        public void ThrowOldBelt(int seed)
        {
            BreakdownNet net = BreakdownNet.Instance;
            Transform slot = net != null && net.BeltSlot != null ? net.BeltSlot : transform;
            var rng = new System.Random(seed);
            GameObject belt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            belt.name = "OldBelt";
            belt.transform.position = slot.position + slot.forward * 0.25f;
            belt.transform.rotation = Quaternion.Euler(90f, (float)rng.NextDouble() * 360f, 0f);
            belt.transform.localScale = new Vector3(0.32f, 0.03f, 0.32f);
            if (_oldBeltMaterial != null) { belt.GetComponent<Renderer>().sharedMaterial = _oldBeltMaterial; }
            var rb = belt.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            Vector3 dir = slot.forward + Vector3.up * (0.6f + (float)rng.NextDouble() * 0.5f) + slot.right * ((float)rng.NextDouble() - 0.5f);
            rb.linearVelocity = dir.normalized * (3.5f + (float)rng.NextDouble() * 2f);
            rb.angularVelocity = new Vector3((float)rng.NextDouble() * 12f, (float)rng.NextDouble() * 12f, (float)rng.NextDouble() * 12f);
            Destroy(belt, BreakdownTuning.Instance.OldPartSeconds);
        }

        private static void Burst(ParticleSystem ps, Vector3 at, int count)
        {
            if (ps == null) { return; }
            ps.transform.position = at;
            ps.Emit(count);
        }

        // ---------- every frame ----------

        private void SetEmission(Renderer r, Color color)
        {
            if (r == null) { return; }
            r.GetPropertyBlock(_block);
            _block.SetColor(EmissionColor, color);
            r.SetPropertyBlock(_block);
        }

        private static void Emit(ParticleSystem ps, bool on)
        {
            if (ps == null) { return; }
            ParticleSystem.EmissionModule e = ps.emission;
            if (e.enabled != on) { e.enabled = on; }
            if (on && !ps.isPlaying) { ps.Play(); }
        }

        private void Update()
        {
            BreakdownNet net = BreakdownNet.Instance;
            bool sputter = net != null && net.IsSpawned && net.IsActive(BreakdownType.Sputter);
            bool belt = net != null && net.IsSpawned && net.IsActive(BreakdownType.Belt);
            bool breaker = net != null && net.IsSpawned && net.IsActive(BreakdownType.Breaker);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 7f);

            // the engine coughs: black smoke and a shake that comes in puffs
            Emit(_blackSmoke, sputter);
            Emit(_greySmoke, belt);
            float shake = 0f;
            if (sputter)
            {
                float cycle = Time.time % _coughSeconds / _coughSeconds;
                shake = cycle > 0.6f ? 1f : 0.25f;
            }
            ShakeDegrees = shake * _shakeDegrees;
            if (_body != null)
            {
                Quaternion jitter = shake > 0f ? Quaternion.Euler(Mathf.Sin(Time.time * 53f) * ShakeDegrees * 0.5f, 0f, Mathf.Sin(Time.time * 61f) * ShakeDegrees) : Quaternion.identity;
                _body.localRotation = _bodyRotation * jitter;
            }

            // the glowing sweet spot (and a flash on a good hit)
            _hitFlash = Mathf.Max(0f, _hitFlash - Time.deltaTime);
            _flash = Mathf.Max(0f, _flash - Time.deltaTime);
            for (int i = 0; _sweetSpotGlows != null && i < _sweetSpotGlows.Length; i++)
            {
                bool glow = sputter && net.SweetSpotIndex == i;
                float k = glow ? (0.7f + 0.6f * pulse) : 0f;
                if (_hitFlash > 0f && i == _hitFlashSpot) { k = Mathf.Max(k, 2.5f); }
                SetEmission(_sweetSpotGlows[i], Lamp * k);
                if (_sweetSpotGlows[i] != null) { _sweetSpotGlows[i].enabled = glow || (_hitFlash > 0f && i == _hitFlashSpot); }
            }

            // the belt slot and the lever pulse while they are what needs doing
            SetEmission(_beltSlotGlow, belt ? Lamp * (0.5f + 1.2f * pulse) : Color.black);
            bool leverDown = breaker && !net.LeverUp;
            SetEmission(_leverKnob, leverDown ? Spark * (0.5f + 1.2f * pulse) : (_flash > 0f ? Lamp : Color.black));
            float leverTarget = breaker && !net.LeverUp ? _leverDownDegrees : _leverUpDegrees;
            _lever = Mathf.MoveTowards(_lever, leverTarget, 400f * Time.deltaTime);
            if (_leverPivot != null) { _leverPivot.localRotation = Quaternion.Euler(0f, 0f, _lever); }

            // one outlet's light goes red
            for (int i = 0; _outletLamps != null && i < _outletLamps.Length; i++)
            {
                bool tripped = breaker && net.TrippedOutlet == i && !net.LeverUp;
                SetEmission(_outletLamps[i], tripped ? Spark * 2f : new Color(0.1f, 0.5f, 0.15f));
                if (_outletLamps[i] != null)
                {
                    _outletLamps[i].GetPropertyBlock(_block);
                    _block.SetColor(BaseColor, tripped ? Spark : new Color(0.1f, 0.35f, 0.15f));
                    _outletLamps[i].SetPropertyBlock(_block);
                }
            }

            // fix progress while somebody holds the belt at the slot / the lever
            SetBar(_beltBar, net != null && belt ? net.BeltProgress01 : 0f);
            SetBar(_resetBar, net != null && breaker && !net.LeverUp ? net.ResetProgress01 : 0f);

            // the heat bar pulses red while the belt is gone
            GeneratorNet gen = GeneratorNet.Instance;
            if (gen != null) { gen.HeatAlarm = belt; }
        }

        private static void SetBar(Transform bar, float fill)
        {
            if (bar == null) { return; }
            bool on = fill > 0.001f;
            if (bar.gameObject.activeSelf != on) { bar.gameObject.SetActive(on); }
            if (on) { bar.localScale = new Vector3(Mathf.Max(0.001f, fill), bar.localScale.y, bar.localScale.z); }
        }
    }
}
