using PowerCutCrew.Core;

namespace PowerCutCrew.Shift
{
    /// <summary>The clipboard next to the generator: "[E] Start shift" in the lobby, from any player (the host decides).</summary>
    public class ShiftClipboard : Interactable
    {
        public override string CurrentPromptKey
        {
            get
            {
                ShiftNet shift = ShiftNet.Instance;
                return shift != null && shift.State == ShiftState.Lobby ? base.CurrentPromptKey : null;
            }
        }

        public override bool IsAvailable(in InteractionContext context)
        {
            ShiftNet shift = ShiftNet.Instance;
            return shift != null && shift.State == ShiftState.Lobby;
        }

        protected override void OnApproved(in InteractionContext context)
        {
            ShiftNet.Instance.HostStart();
        }
    }
}
