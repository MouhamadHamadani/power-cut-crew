using PowerCutCrew.Buildings;
using PowerCutCrew.Cables;
using PowerCutCrew.Core;
using PowerCutCrew.Net;
using UnityEngine;

namespace PowerCutCrew.Shift
{
    /// <summary>
    /// Bot behaviour for the shift. With <c>-autostart</c> a bot starts the shift at the clipboard (and, with <c>-shifts n</c>, presses "Another shift" after each
    /// summary until n shifts have been played). It also tells the core-loop bot where plugs are wanted: the building that is Requesting and in the dark,
    /// and which plug can be moved away because its building is Quiet.
    /// </summary>
    public class ShiftBot : IBotBehaviour
    {
        private readonly bool _autostart = LaunchArgs.Has("-autostart");
        private readonly int _shifts = LaunchArgs.Has("-shifts") ? int.Parse(LaunchArgs.Get("-shifts"), System.Globalization.CultureInfo.InvariantCulture) : 1;
        private float _wait;
        private int _summariesSeen;
        private ShiftState _lastState = (ShiftState)255;
        private float _summaryAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            BotExtensions.Factories.Insert(0, seed => new ShiftBot());   // before the core loop: starting the shift comes first
            CoreLoopBot.ChooseFreeSocket = ChooseFreeSocket;
            CoreLoopBot.ChooseSocketToUnplug = ChooseSocketToUnplug;
            CoreLoopBot.PlugWanted = PlugWanted;
        }

        // ---------- what the core-loop bot asks ----------

        private static bool ShiftRunning => ShiftNet.Instance != null && ShiftNet.Instance.State == ShiftState.Running;

        private static bool Needy(BuildingNet b) => b.IsSpawned && b.Requesting && !b.Powered && b.Socket != null && !b.Socket.Occupied;

        private static bool PlugWanted()
        {
            if (!ShiftRunning) { return false; }
            foreach (BuildingNet b in BuildingNet.All)
            {
                if (Needy(b)) { return true; }
            }
            return false;
        }

        /// <summary>The hungriest building in the dark (the least happy one first), now and then a random one so overloads and wasted trips still happen.</summary>
        private static PowerSocket ChooseFreeSocket(System.Random rng)
        {
            BuildingNet best = null;
            var needy = new System.Collections.Generic.List<BuildingNet>();
            foreach (BuildingNet b in BuildingNet.All)
            {
                if (!Needy(b)) { continue; }
                needy.Add(b);
                if (best == null || (best.Angry && !b.Angry) || (best.Angry == b.Angry && b.Happiness < best.Happiness)) { best = b; }
            }
            if (needy.Count == 0) { return null; }
            BuildingNet pick = rng.NextDouble() < 0.8 ? best : needy[rng.Next(needy.Count)];
            return pick.Socket;
        }

        /// <summary>A plugged cable whose building is Quiet, while another building waits in the dark: that plug should move.</summary>
        private static PowerSocket ChooseSocketToUnplug()
        {
            if (!ShiftRunning) { return null; }
            bool somebodyWaits = false;
            foreach (BuildingNet b in BuildingNet.All)
            {
                if (Needy(b)) { somebodyWaits = true; break; }
            }
            if (!somebodyWaits) { return null; }
            foreach (BuildingNet b in BuildingNet.All)
            {
                if (b.IsSpawned && !b.Requesting && b.Socket != null && b.Socket.Occupied) { return b.Socket; }
            }
            return null;
        }

        // ---------- this bot ----------

        public bool Act(PlayerController me, ref PlayerFrame f, float dt)
        {
            ShiftNet shift = ShiftNet.Instance;
            if (shift == null || !shift.IsSpawned) { return false; }
            ShiftState state = shift.State;
            if (state != _lastState)
            {
                if (state == ShiftState.Summary) { _summaryAt = Time.realtimeSinceStartup; _summariesSeen++; }
                _lastState = state;
                _wait = 0f;
            }

            if (_autostart && shift.IsServer && state == ShiftState.Summary && _summariesSeen < _shifts && Time.realtimeSinceStartup - _summaryAt > 6f)
            {
                shift.HostAnotherShift();   // the summary screen's "Another shift" button
                return false;
            }
            if (!_autostart || state != ShiftState.Lobby) { return false; }

            _wait += dt;
            if (_wait < 4f) { return false; }   // let the others arrive and the generator get going
            ShiftClipboard clipboard = Object.FindFirstObjectByType<ShiftClipboard>();
            if (clipboard == null) { return false; }
            Vector3 stand = clipboard.transform.position + (me.transform.position - clipboard.transform.position).normalized * 1.3f;
            stand.y = 0f;
            if (BotBrain.Walk(me, stand, ref f, dt, 0.6f) && BotBrain.AimAt(me, clipboard.transform.position + Vector3.up * 0.9f, ref f, dt) < 6f)
            {
                if (InteractionFocus.Current is ShiftClipboard) { f.Interact = true; }
            }
            return true;
        }
    }
}
