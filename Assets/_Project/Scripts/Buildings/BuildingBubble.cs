using PowerCutCrew.Localization;
using UnityEngine;

namespace PowerCutCrew.Buildings
{
    /// <summary>A neighbor's speech bubble (world-space, faces the camera). Shows one localized line while it is visible.</summary>
    public class BuildingBubble : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private LocalizedRtlText _text;
        [SerializeField] private float _screenScale = 0.05f;
        [SerializeField] private float _minScale = 0.5f;
        [SerializeField] private float _maxScale = 3f;

        /// <summary>Doc screenshots: the camera the bubble faces and is sized for (null = the main camera).</summary>
        public static Camera CameraOverride;

        private string _key;
        private Camera _camera;
        private float _baseScale;   // the canvas scale from the prefab (pixels to metres); the screen scale multiplies it
        private float _cameraSearch;

        public bool Visible => _root != null && _root.activeSelf;
        public string Key => _key;

        public void Show(string key)
        {
            if (key != _key)
            {
                _key = key;
                _text.SetKey(key);
            }
            if (!_root.activeSelf) { _root.SetActive(true); }
        }

        public void Hide()
        {
            if (_root.activeSelf) { _root.SetActive(false); }
            _key = null;
        }

        private void LateUpdate()
        {
            if (!_root.activeSelf) { return; }
            if (_camera == null && Time.unscaledTime >= _cameraSearch)
            {
                _camera = Camera.main;
                _cameraSearch = Time.unscaledTime + 1f;
            }
            Camera cam = CameraOverride != null ? CameraOverride : _camera;
            if (cam == null) { return; }
            Vector3 toBubble = transform.position - cam.transform.position;
            transform.rotation = Quaternion.LookRotation(toBubble, Vector3.up);
            transform.localScale = Vector3.one * (_baseScale > 0f ? _baseScale : (_baseScale = transform.localScale.x)) * Mathf.Clamp(toBubble.magnitude * _screenScale, _minScale, _maxScale);
        }
    }
}
