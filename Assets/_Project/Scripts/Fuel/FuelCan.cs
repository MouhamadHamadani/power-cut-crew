using PowerCutCrew.Core;
using PowerCutCrew.Net;
using Unity.Netcode;
using UnityEngine;

namespace PowerCutCrew.Fuel
{
    /// <summary>
    /// Fuel in a jerrycan. The host owns the amount (a plain <see cref="FuelContainer"/>) and publishes it as a
    /// quantised NetworkVariable at a low rate. A small bar on the can (also on the holder's local copy) shows the fill.
    /// </summary>
    [RequireComponent(typeof(NetworkPhysicsItem))]
    public class FuelCan : NetworkBehaviour, IFuelContainerView
    {
        private readonly NetworkVariable<float> _fuel = new NetworkVariable<float>(0f);

        private FuelContainer _container;
        private NetworkPhysicsItem _item;
        private float _nextSync;
        private bool _dirty;
        private float _shownFill = -1f;
        private GameObject _shownCopy;

        public float Capacity => FuelTuning.Instance.CanCapacity;

        /// <summary>The fuel in the can as this machine knows it (host: exact, clients: last synced).</summary>
        public float Amount => IsServer && _container != null ? _container.Amount : _fuel.Value;

        public FuelContainer HostContainer => _container;

        public NetworkPhysicsItem Item => _item;

        private void Awake()
        {
            _item = GetComponent<NetworkPhysicsItem>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) { _container = new FuelContainer(Capacity, _fuel.Value); }
        }

        /// <summary>Host: set the fuel (scene placement: full or empty).</summary>
        public void HostSet(float amount)
        {
            _container ??= new FuelContainer(Capacity);
            _container.Set(amount);
            _dirty = true;
            Publish(true);
        }

        /// <summary>Host: add fuel; returns what was accepted.</summary>
        public float HostAdd(float amount)
        {
            float accepted = _container.Add(amount);
            if (accepted > 0f) { _dirty = true; }
            return accepted;
        }

        /// <summary>Host: take fuel out; returns what was taken.</summary>
        public float HostTake(float amount)
        {
            float taken = _container.Take(amount);
            if (taken > 0f) { _dirty = true; }
            return taken;
        }

        private void Publish(bool force)
        {
            FuelTuning t = FuelTuning.Instance;
            if (!force && Time.unscaledTime < _nextSync) { return; }
            _nextSync = Time.unscaledTime + t.SyncInterval;
            float q = Mathf.Round(_container.Amount / t.SyncStep) * t.SyncStep;
            if (_container.IsEmpty) { q = 0f; }
            if (_container.IsFull) { q = _container.Capacity; }
            if (!Mathf.Approximately(q, _fuel.Value)) { _fuel.Value = q; }
            _dirty = false;
        }

        private void Update()
        {
            if (IsSpawned && IsServer && _dirty) { Publish(false); }
        }

        private void LateUpdate()
        {
            float fill = Capacity > 0f ? Mathf.Clamp01(Amount / Capacity) : 0f;
            GameObject copy = _item.HeldCopy;
            if (Mathf.Approximately(fill, _shownFill) && copy == _shownCopy) { return; }
            _shownFill = fill;
            _shownCopy = copy;
            ApplyBar(_item.VisualRoot, fill);
            if (copy != null) { ApplyBar(copy.transform, fill); }
        }

        /// <summary>The bar is a child named FuelBar of the visual: scaled in height and tinted from grey (empty) to orange (full).</summary>
        private static void ApplyBar(Transform visualRoot, float fill)
        {
            if (visualRoot == null) { return; }
            Transform bar = visualRoot.Find("FuelBar");
            if (bar == null) { return; }
            Vector3 s = bar.localScale;
            bar.localScale = new Vector3(s.x, Mathf.Max(0.001f, 0.9f * fill), s.z);
            bar.localPosition = new Vector3(bar.localPosition.x, -0.45f + 0.45f * fill, bar.localPosition.z);
            var r = bar.GetComponent<Renderer>();
            if (r != null)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", Color.Lerp(new Color(0.3f, 0.3f, 0.32f), new Color(1f, 0.55f, 0.1f), fill));
                r.SetPropertyBlock(block);
            }
        }
    }
}
