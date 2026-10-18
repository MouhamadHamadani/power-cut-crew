using System.Globalization;
using PowerCutCrew.Buildings;
using PowerCutCrew.Cables;
using PowerCutCrew.Core;
using PowerCutCrew.Generator;
using PowerCutCrew.Localization;
using PowerCutCrew.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace PowerCutCrew.Shift
{
    /// <summary>
    /// The help part of the shift screen: the legend of the four marker icons (lobby and countdown), the controls card (the Help key, listing the actions with their
    /// current bindings), the small "H: controls" hint, and the first-shift tips. Everything is local to this machine. A new session means a new HUD, so the tips and
    /// the legend start over; "Another shift" keeps them. Arabic mirrors the layout (rows, icons and the corner hint swap sides).
    /// </summary>
    public class HelpHud : MonoBehaviour
    {
        /// <summary>The help of this process (doc screenshots open the card through it).</summary>
        public static HelpHud Instance { get; private set; }

        private const string HintPref = "pcc.cornerHint";

        [SerializeField] private GameObject _legend;
        [SerializeField] private LocalizedRtlText _hint;
        [SerializeField] private GameObject _card;
        [Tooltip("In the order Move, Look, Jump, Sprint, Crouch, Interact, Grab, UseHit, Drop, Help.")]
        [SerializeField] private LocalizedRtlText[] _rowBindings = new LocalizedRtlText[10];
        [SerializeField] private LocalizedRtlText _closeText;
        [SerializeField] private Button _hintButton;
        [SerializeField] private LocalizedRtlText _hintButtonText;
        [SerializeField] private GameObject _tip;
        [SerializeField] private LocalizedRtlText _tipText;
        [Tooltip("Every rectangle that swaps sides in Arabic (its x position and anchors are mirrored).")]
        [SerializeField] private RectTransform[] _mirror;

        private ShiftNet _shift;
        private PlayerControls _controls;
        private InputAction _help;   // found by name: the Help action exists in the asset whatever the generated wrapper looks like
        private TipLogic _tips;
        private bool _hintOn = true;
        private bool _cardOpen;
        private bool _mirrored;
        private bool _captured;
        private ShiftState _stateShown = (ShiftState)255;
        private string _tipShown;

        public bool CardOpen => _cardOpen;
        public TipLogic Tips => _tips;
        public string TipKeyShown => _tipShown;

        public void Bind(ShiftNet shift)
        {
            _shift = shift;
            Instance = this;
            _tips = new TipLogic(HelpTuning.Instance.ToSettings());
            _controls = new PlayerControls();
            _help = _controls.asset.FindAction("Player/Help", true);
            _help.Enable();
            _help.performed += OnHelp;
            _hintOn = SafeGetHint();
            if (_hintButton != null) { _hintButton.onClick.AddListener(ToggleHint); }
            _card.SetActive(false);
            _tip.SetActive(false);
            FillBindings();
            ApplyHintText();
        }

        private void OnHelp(InputAction.CallbackContext ctx)
        {
            SetCardOpen(!_cardOpen);
        }

        public void SetCardOpen(bool open)
        {
            _cardOpen = open;
            if (_card != null) { _card.SetActive(open); }
            bool summary = Object.FindFirstObjectByType<ShiftHud>() is ShiftHud h && h.SummaryVisible;
            SetCapture(open || summary);
            if (open) { FillBindings(); ApplyMirror(); }
        }

        private void SetCapture(bool on)
        {
            if (_captured == on) { return; }
            _captured = on;
            HumanInput.UiCapture = on;   // the card has a button: the mouse must be free while it is open
        }

        private static bool SafeGetHint()
        {
            try { return PlayerPrefs.GetInt(HintPref, 1) != 0; }
            catch { return true; }
        }

        private void ToggleHint()
        {
            _hintOn = !_hintOn;
            try { PlayerPrefs.SetInt(HintPref, _hintOn ? 1 : 0); }
            catch { /* the choice just lasts until the game closes */ }
            ApplyHintText();
        }

        private void ApplyHintText()
        {
            if (_hintButtonText != null) { _hintButtonText.SetKey(_hintOn ? "help.hint_hide" : "help.hint_show"); }
        }

        // ---------- bindings ----------

        /// <summary>The display text of an action's bindings in one control scheme; a composite (Move) is spelled out part by part, up / left / down / right.</summary>
        private static string Display(InputAction a, string group)
        {
            string text = DisplayRaw(a, group);
            if (group != "Gamepad" || string.IsNullOrEmpty(text)) { return text; }
            string[] parts = text.Split('/');
            for (int i = 0; i < parts.Length; i++) { parts[i] = InputNames.ShortGamepad(parts[i].Trim()); }
            return string.Join("/", parts);
        }

        private static string DisplayRaw(InputAction a, string group)
        {
            string whole = a.GetBindingDisplayString(InputBinding.MaskByGroup(group));
            if (!string.IsNullOrEmpty(whole)) { return whole; }
            var parts = new string[4];
            int found = 0;
            for (int i = 0; i < a.bindings.Count; i++)
            {
                InputBinding b = a.bindings[i];
                if (!b.isPartOfComposite || string.IsNullOrEmpty(b.groups) || !b.groups.Contains(group)) { continue; }
                int slot = b.name == "up" ? 0 : b.name == "left" ? 1 : b.name == "down" ? 2 : b.name == "right" ? 3 : -1;
                if (slot < 0) { continue; }
                parts[slot] = a.GetBindingDisplayString(i, InputBinding.DisplayStringOptions.DontIncludeInteractions);
                found++;
            }
            return found == 0 ? string.Empty : string.Join("/", System.Array.FindAll(parts, p => !string.IsNullOrEmpty(p)));
        }

        private static string KeyboardAndGamepad(InputAction a)
        {
            string kb = Display(a, "Keyboard&Mouse");
            string gp = Display(a, "Gamepad");
            if (string.IsNullOrEmpty(kb)) { return gp; }
            return string.IsNullOrEmpty(gp) ? kb : kb + " / " + gp;
        }

        private InputAction[] Actions()
        {
            PlayerControls.PlayerActions p = _controls.Player;
            return new[] { p.Move, p.Look, p.Jump, p.Sprint, p.Crouch, p.Interact, p.Grab, p.UseHit, p.Drop, _help };
        }

        /// <summary>The text of the Keyboard / gamepad bindings of one action, for the prompts that name a key.</summary>
        public string BindingOf(InputAction a) => KeyboardAndGamepad(a);

        private void FillBindings()
        {
            if (_controls == null) { return; }
            InputAction[] actions = Actions();
            for (int i = 0; i < actions.Length && i < _rowBindings.Length; i++)
            {
                LocalizedRtlText label = _rowBindings[i];
                if (label == null) { continue; }
                string gamepad = Display(actions[i], "Gamepad");
                if (i == 1)
                {
                    // the mouse look is a pointer delta: name the device, not "Delta"
                    label.SetKey("help.mouse");
                    label.Format = text => text + " / " + gamepad;
                }
                else
                {
                    string both = KeyboardAndGamepad(actions[i]);
                    label.SetKey("help.binding");
                    label.Format = text => string.Format(CultureInfo.InvariantCulture, text, both);
                }
                label.Refresh();
            }
            string helpKey = Display(actions[9], "Keyboard&Mouse");
            _hint.Format = text => RtlBinding.Compose(helpKey, text, BindingStyle.Colon);
            _hint.Refresh();
            if (_closeText != null)
            {
                _closeText.Format = text => RtlBinding.Compose(helpKey, text, BindingStyle.Colon);
                _closeText.Refresh();
            }
        }

        // ---------- every frame ----------

        private void Update()
        {
            if (_shift == null || !_shift.IsSpawned) { return; }
            ShiftState state = _shift.State;
            if (state != _stateShown)
            {
                _stateShown = state;
                _legend.SetActive(state == ShiftState.Lobby || state == ShiftState.Countdown);
            }
            _hint.gameObject.SetActive(_hintOn && (state == ShiftState.Countdown || state == ShiftState.Running) && !_cardOpen);
            ApplyMirror();
            UpdateTips(state);
        }

        private void UpdateTips(ShiftState state)
        {
            GeneratorNet gen = GeneratorNet.Instance;
            var inputs = new TipInputs
            {
                Active = state == ShiftState.Running && !_cardOpen,
                FuelFraction = gen != null && gen.Capacity > 0f ? gen.Fuel / gen.Capacity : 1f,
                Heat = gen != null ? gen.Heat : 0f,
            };
            foreach (BuildingNet b in BuildingNet.All)
            {
                if (b != null && b.IsSpawned && b.Marker == MarkerState.Unpowered) { inputs.AnyUnpowered = true; break; }
            }
            NetPlayer me = NetPlayer.Local;
            if (me != null)
            {
                float near = HelpTuning.Instance.PlugNearMeters;
                foreach (CableNet c in CableNet.All)
                {
                    if (c == null || c.Item == null || c.Item.IsHeld || c.Item.Locked) { continue; }
                    if (c.State != CableState.Fresh && c.State != CableState.Loose) { continue; }
                    if (Vector3.Distance(me.transform.position, c.transform.position) < near) { inputs.NearFreePlug = true; break; }
                }
            }
            Tip? started = _tips.Update(Time.deltaTime, inputs);
            if (started.HasValue && _controls != null)
            {
                string grab = KeyboardAndGamepad(_controls.Player.Grab);
                _tipText.SetKey(TipLogic.Key(started.Value));
                // only the plug tip names a key; the others are plain sentences
                if (started.Value == Tip.Plug) { _tipText.Format = text => RtlBinding.Compose(grab, text); }
                else { _tipText.Format = null; }
                _tipText.Refresh();
                _tipShown = TipLogic.Key(started.Value);
            }
            bool show = _tips.Current.HasValue;
            if (_tip.activeSelf != show) { _tip.SetActive(show); }
            if (!show) { _tipShown = null; }
        }

        private void ApplyMirror()
        {
            bool arabic = LocalizationSettings.SelectedLocale != null && LocalizationSettings.SelectedLocale.Identifier.Code == "ar";
            if (arabic == _mirrored) { return; }
            _mirrored = arabic;
            foreach (RectTransform rt in _mirror)
            {
                if (rt == null) { continue; }
                rt.anchorMin = new Vector2(1f - rt.anchorMin.x, rt.anchorMin.y);
                rt.anchorMax = new Vector2(1f - rt.anchorMax.x, rt.anchorMax.y);
                rt.pivot = new Vector2(1f - rt.pivot.x, rt.pivot.y);
                rt.anchoredPosition = new Vector2(-rt.anchoredPosition.x, rt.anchoredPosition.y);
            }
        }

        private void OnDestroy()
        {
            if (_controls != null)
            {
                _help.performed -= OnHelp;
                _help.Disable();
                _controls.Player.Disable();   // Dispose logs an error for a map that is still enabled
                _controls.Dispose();
            }
            if (_captured) { HumanInput.UiCapture = false; }
            if (Instance == this) { Instance = null; }
        }
    }
}
