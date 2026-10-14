using System.Globalization;
using PowerCutCrew.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace PowerCutCrew.Generator
{
    /// <summary>World-space readout above the generator: state, fuel bar, heat bar and "load / rated A". Everything localized (EN / AR).</summary>
    public class GeneratorPanel : MonoBehaviour
    {
        [SerializeField] private LocalizedRtlText _stateLabel;
        [SerializeField] private LocalizedRtlText _loadLabel;
        [SerializeField] private RectTransform _fuelBar;
        [SerializeField] private RectTransform _heatBar;
        [SerializeField] private Image _heatImage;

        private string _stateKey;
        private float _shownLoad = -1f;
        private float _shownRated = -1f;

        private static string KeyFor(GeneratorState s)
        {
            switch (s)
            {
                case GeneratorState.Running: return "gen.state.running";
                case GeneratorState.OutOfFuel: return "gen.state.out_of_fuel";
                case GeneratorState.Overheated: return "gen.state.overheated";
                default: return "gen.state.off";
            }
        }

        /// <summary>Bars are children of a 250 px wide background, anchored left: the width is the fill.</summary>
        private static void SetBar(RectTransform bar, float fill)
        {
            float full = ((RectTransform)bar.parent).rect.width;
            bar.sizeDelta = new Vector2(Mathf.Max(1f, full * Mathf.Clamp01(fill)), 0f);
        }

        private void Update()
        {
            GeneratorNet gen = GeneratorNet.Instance;
            if (gen == null) { return; }
            string key = KeyFor(gen.State);
            if (key != _stateKey)
            {
                _stateKey = key;
                _stateLabel.SetKey(key);
            }
            SetBar(_fuelBar, gen.Fuel / gen.Capacity);
            float heat01 = gen.Heat / 100f;
            SetBar(_heatBar, heat01);
            _heatImage.color = Color.Lerp(new Color(0.2f, 0.8f, 0.3f), new Color(0.95f, 0.15f, 0.1f), heat01);

            float load = gen.Load;
            float rated = gen.RatedAmps;
            if (!Mathf.Approximately(load, _shownLoad) || !Mathf.Approximately(rated, _shownRated))
            {
                _shownLoad = load;
                _shownRated = rated;
                _loadLabel.Format = text => string.Format(text, load.ToString("0.#", CultureInfo.InvariantCulture), rated.ToString("0.#", CultureInfo.InvariantCulture));
                _loadLabel.Refresh();
            }
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam != null) { transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position); }
        }
    }
}
