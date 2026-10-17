using System.Globalization;
using PowerCutCrew.Generator;
using PowerCutCrew.Localization;
using PowerCutCrew.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PowerCutCrew.Shift
{
    /// <summary>
    /// The shift's screen: the HUD strip at the top (time left, crew money, generator load against its rating), the lobby hint, the big 3-2-1,
    /// and the summary screen with its verdict (everyone sees the same numbers; only the host gets the buttons). Also dims the street while the
    /// power cut runs. Everything is localized and RTL-correct.
    /// </summary>
    public class ShiftHud : MonoBehaviour
    {
        [Header("HUD strip")]
        [SerializeField] private LocalizedRtlText _time;
        [SerializeField] private LocalizedRtlText _money;
        [SerializeField] private LocalizedRtlText _load;
        [SerializeField] private LocalizedRtlText _lobbyHint;
        [SerializeField] private TMP_Text _countdown;

        [Header("Summary")]
        [SerializeField] private GameObject _summaryRoot;
        [SerializeField] private LocalizedRtlText _sumUptime;
        [SerializeField] private LocalizedRtlText _sumMoney;
        [SerializeField] private LocalizedRtlText _sumAngry;
        [SerializeField] private LocalizedRtlText _sumOverheats;
        [SerializeField] private LocalizedRtlText _sumSnaps;
        [SerializeField] private LocalizedRtlText _sumPulls;
        [SerializeField] private LocalizedRtlText _sumVerdict;
        [SerializeField] private GameObject _hostButtons;
        [SerializeField] private GameObject _waiting;
        [SerializeField] private Button _againButton;
        [SerializeField] private Button _menuButton;

        private ShiftNet _shift;
        private string _timeShown, _moneyShown, _loadShown;
        private ShiftState _stateShown = (ShiftState)255;
        private ShiftSummaryData _summaryShown;
        private bool _captured;
        private float _mood;
        private Light _sun;
        private float _sunDay;
        private Color _ambientDay;
        private UnityEngine.Rendering.AmbientMode _ambientModeDay;

        public bool SummaryVisible => _summaryRoot != null && _summaryRoot.activeSelf;
        public string ShownTime => _timeShown;
        public string ShownMoney => _moneyShown;

        public void Bind(ShiftNet shift)
        {
            _shift = shift;
            if (_againButton != null) { _againButton.onClick.AddListener(() => _shift.HostAnotherShift()); }
            if (_menuButton != null) { _menuButton.onClick.AddListener(() => { if (NetSession.Instance != null) { NetSession.Instance.LeaveToMenu(); } }); }
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            _sun = RenderSettings.sun != null ? RenderSettings.sun : FindSun();
            if (_sun != null) { _sunDay = _sun.intensity; }
            _ambientDay = RenderSettings.ambientLight;
            _ambientModeDay = RenderSettings.ambientMode;
            _summaryRoot.SetActive(false);
            _countdown.gameObject.SetActive(false);
        }

        private static Light FindSun()
        {
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) { return l; }
            }
            return null;
        }

        private static void SetValues(LocalizedRtlText label, ref string shown, string value, params object[] args)
        {
            if (shown == value) { return; }
            shown = value;
            label.Format = text => string.Format(CultureInfo.InvariantCulture, text, args);
            label.Refresh();
        }

        private string _sumUptimeShown, _sumMoneyShown, _sumAngryShown, _sumOverheatsShown, _sumSnapsShown, _sumPullsShown, _verdictShown;

        private void Update()
        {
            if (_shift == null || !_shift.IsSpawned) { return; }
            ShiftState state = _shift.State;

            // strip
            int seconds = Mathf.Max(0, Mathf.CeilToInt(state == ShiftState.Lobby ? _shift.Duration : _shift.TimeLeft));
            string time = $"{seconds / 60}:{seconds % 60:00}";
            if (time != _timeShown)
            {
                _timeShown = time;
                _time.Format = text => string.Format(CultureInfo.InvariantCulture, text, time);
                _time.Refresh();
            }
            string money = _shift.Money.ToString(CultureInfo.InvariantCulture);
            if (money != _moneyShown)
            {
                _moneyShown = money;
                _money.Format = text => string.Format(CultureInfo.InvariantCulture, text, money);
                _money.Refresh();
            }
            GeneratorNet gen = GeneratorNet.Instance;
            if (gen != null)
            {
                string load = $"{gen.Load.ToString("0.#", CultureInfo.InvariantCulture)}|{gen.RatedAmps.ToString("0.#", CultureInfo.InvariantCulture)}";
                if (load != _loadShown)
                {
                    _loadShown = load;
                    string[] parts = load.Split('|');
                    _load.Format = text => string.Format(CultureInfo.InvariantCulture, text, parts[0], parts[1]);
                    _load.Refresh();
                }
            }

            if (state != _stateShown)
            {
                _stateShown = state;
                _lobbyHint.gameObject.SetActive(state == ShiftState.Lobby);
                bool summary = state == ShiftState.Summary;
                _summaryRoot.SetActive(summary);
                bool host = _shift.IsServer;
                _hostButtons.SetActive(summary && host);
                _waiting.SetActive(summary && !host);
                SetCapture(summary && host);
            }
            if (state == ShiftState.Countdown)
            {
                _countdown.gameObject.SetActive(true);
                _countdown.text = _shift.Countdown.ToString(CultureInfo.InvariantCulture);
            }
            else if (_countdown.gameObject.activeSelf)
            {
                _countdown.gameObject.SetActive(false);
            }
            if (state == ShiftState.Summary) { UpdateSummary(); }
            UpdateMood(state);
        }

        private void UpdateSummary()
        {
            ShiftSummaryData s = _shift.Summary;
            SetValues(_sumUptime, ref _sumUptimeShown, s.UptimePercent.ToString("0.#", CultureInfo.InvariantCulture), s.UptimePercent.ToString("0.#", CultureInfo.InvariantCulture));
            SetValues(_sumMoney, ref _sumMoneyShown, s.Money.ToString(CultureInfo.InvariantCulture), s.Money);
            SetValues(_sumAngry, ref _sumAngryShown, s.AngryBuildings.ToString(CultureInfo.InvariantCulture), s.AngryBuildings);
            SetValues(_sumOverheats, ref _sumOverheatsShown, s.Overheats.ToString(CultureInfo.InvariantCulture), s.Overheats);
            SetValues(_sumSnaps, ref _sumSnapsShown, s.Snaps.ToString(CultureInfo.InvariantCulture), s.Snaps);
            SetValues(_sumPulls, ref _sumPullsShown, s.FailedPulls.ToString(CultureInfo.InvariantCulture), s.FailedPulls);
            string verdictKey = VerdictKey((ShiftVerdict)s.Verdict);
            if (verdictKey != _verdictShown)
            {
                _verdictShown = verdictKey;
                _sumVerdict.SetKey(verdictKey);
            }
        }

        public static string VerdictKey(ShiftVerdict v)
        {
            switch (v)
            {
                case ShiftVerdict.Loved: return "shift.verdict.loved";
                case ShiftVerdict.Good: return "shift.verdict.good";
                case ShiftVerdict.Meh: return "shift.verdict.meh";
                default: return "shift.verdict.bad";
            }
        }

        private void SetCapture(bool on)
        {
            if (_captured == on) { return; }
            _captured = on;
            HumanInput.UiCapture = on;   // the summary's buttons need the mouse
        }

        /// <summary>While the power cut runs the street goes dark.</summary>
        private void UpdateMood(ShiftState state)
        {
            ShiftTuning t = ShiftTuning.Instance;
            float target = state == ShiftState.Running ? 1f : 0f;
            _mood = Mathf.MoveTowards(_mood, target, Time.deltaTime / Mathf.Max(0.1f, t.MoodSeconds));
            if (_mood <= 0f && target <= 0f)
            {
                if (RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat)
                {
                    RenderSettings.ambientMode = _ambientModeDay;
                    RenderSettings.ambientLight = _ambientDay;
                    if (_sun != null) { _sun.intensity = _sunDay; }
                }
                return;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(t.DayAmbient, t.NightAmbient, _mood);
            if (_sun != null) { _sun.intensity = Mathf.Lerp(_sunDay, _sunDay * t.NightSunIntensity, _mood); }
        }

        private void OnDestroy()
        {
            if (_captured) { HumanInput.UiCapture = false; }
            if (_mood > 0f)
            {
                RenderSettings.ambientMode = _ambientModeDay;
                RenderSettings.ambientLight = _ambientDay;
                if (_sun != null) { _sun.intensity = _sunDay; }
            }
        }
    }
}
