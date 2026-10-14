using PowerCutCrew.Core;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>One frame of player intent, from a human or from a bot.</summary>
    public struct PlayerFrame
    {
        public Vector2 Move;        // x = strafe, y = forward
        public float YawDelta;      // degrees this frame
        public float PitchDelta;    // degrees this frame (positive looks down)
        public bool Jump, Sprint, Crouch;
        public bool Interact;       // E or LMB pressed this frame
        public bool InteractHeld;   // E held (hold interactions: pour, fill)
        public bool Drop;           // Q pressed this frame
        public bool Throw;          // RMB pressed this frame
    }

    public interface IPlayerInput
    {
        PlayerFrame Read(PlayerController player, float deltaTime);
    }

    /// <summary>Keyboard/mouse/gamepad through the generated PlayerControls wrapper.</summary>
    public class HumanInput : IPlayerInput, System.IDisposable
    {
        private readonly PlayerControls _controls = new PlayerControls();

        public HumanInput()
        {
            _controls.Player.Enable();
            Cursor.lockState = CursorLockMode.Locked;
        }

        public PlayerFrame Read(PlayerController player, float deltaTime)
        {
            var p = _controls.Player;
            Vector2 look = p.Look.ReadValue<Vector2>() * PlayerTuning.Instance.LookSensitivity * 10f;
            return new PlayerFrame
            {
                Move = p.Move.ReadValue<Vector2>(),
                YawDelta = look.x,
                PitchDelta = -look.y,
                Jump = p.Jump.IsPressed(),
                Sprint = p.Sprint.IsPressed(),
                Crouch = p.Crouch.IsPressed(),
                Interact = p.Interact.WasPressedThisFrame() || p.Grab.WasPressedThisFrame(),
                InteractHeld = p.Interact.IsPressed(),
                Drop = p.Drop.WasPressedThisFrame(),
                Throw = p.UseHit.WasPressedThisFrame(),
            };
        }

        public void Dispose()
        {
            _controls.Dispose();
        }
    }
}
