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

        private string _key;
        private Camera _camera;
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
            if (_camera == null) { return; }
            Vector3 toBubble = transform.position - _camera.transform.position;
            transform.rotation = Quaternion.LookRotation(toBubble, Vector3.up);
            transform.localScale = Vector3.one * Mathf.Clamp(toBubble.magnitude * _screenScale, _minScale, _maxScale);
        }
    }
}
