using PowerCutCrew.Core;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>
    /// First-person controller. Runs on the owner only (owner-authoritative movement); other machines receive the
    /// transform from NetworkTransform. Also does the interaction raycast and reports requests to the NetPlayer.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private Transform _cameraTarget;
        [SerializeField] private Transform _hand;

        private CharacterController _cc;
        private IPlayerInput _input;
        private NetPlayer _net;
        private float _pitch;
        private float _vy;
        private float _eyeHeight;
        private LayerMask _interactMask = Physics.DefaultRaycastLayers;
        private Interactable _holdTarget;
        private float _holdTimer;

        public Transform CameraTarget => _cameraTarget;

        /// <summary>Where the held item is shown on the owner's machine (child of the camera target).</summary>
        public Transform Hand => _hand;

        public float Pitch => _pitch;

        public bool IsOwner { get; private set; }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _net = GetComponent<NetPlayer>();
            _eyeHeight = PlayerTuning.Instance.EyeHeightStanding;
        }

        public void SetOwner(bool owner, IPlayerInput input)
        {
            IsOwner = owner;
            _input = input;
            _cc.enabled = owner;
            enabled = owner;
        }

        /// <summary>Teleport an owner (spawn). The CharacterController must be off while the position is set.</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            bool was = _cc.enabled;
            _cc.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _cc.enabled = was;
        }

        private void Update()
        {
            if (_input == null) { return; }
            PlayerTuning t = PlayerTuning.Instance;
            PlayerFrame f = _input.Read(this, Time.deltaTime);

            transform.Rotate(0f, f.YawDelta, 0f);
            _pitch = Mathf.Clamp(_pitch + f.PitchDelta, -t.MaxPitch, t.MaxPitch);

            bool grounded = _cc.isGrounded;
            float targetHeight = f.Crouch ? t.CrouchHeight : t.StandHeight;
            _cc.height = Mathf.MoveTowards(_cc.height, targetHeight, 6f * Time.deltaTime);
            _cc.center = new Vector3(0f, _cc.height * 0.5f, 0f);
            _eyeHeight = Mathf.MoveTowards(_eyeHeight, f.Crouch ? t.EyeHeightCrouching : t.EyeHeightStanding, 4f * Time.deltaTime);
            _cameraTarget.localPosition = new Vector3(0f, _eyeHeight, 0f);
            _cameraTarget.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            float speed = f.Crouch ? t.CrouchSpeed : (f.Sprint ? t.SprintSpeed : t.WalkSpeed);
            Vector3 move = transform.right * f.Move.x + transform.forward * f.Move.y;
            if (move.sqrMagnitude > 1f) { move.Normalize(); }
            if (grounded && _vy < 0f) { _vy = -2f; }
            if (f.Jump && grounded && !f.Crouch) { _vy = t.JumpSpeed; }
            _vy += t.Gravity * Time.deltaTime;
            _cc.Move((move * speed + Vector3.up * _vy) * Time.deltaTime);

            UpdateInteraction(t, f);
        }

        private void UpdateInteraction(PlayerTuning t, PlayerFrame f)
        {
            Interactable focus = null;
            var ray = new Ray(_cameraTarget.position, _cameraTarget.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, t.InteractRange, _interactMask, QueryTriggerInteraction.Ignore))
            {
                focus = hit.collider.GetComponentInParent<Interactable>();
                if (focus != null && (hit.distance > focus.MaxRange || focus.CurrentPromptKey == null)) { focus = null; }
            }
            InteractionFocus.Set(focus);

            if (_net == null) { return; }
            UpdateHold(focus, f);
            if (f.Interact && focus != null && !focus.IsHold) { _net.RequestInteract(focus); }
            if (f.Drop) { _net.RequestDrop(); }
            if (f.Throw)
            {
                // a usable item (the wrench) is used with the throw button; everything else is thrown
                NetworkPhysicsItem held = NetworkPhysicsItem.HeldByLocal();
                IUsableItem usable = held != null ? held.GetComponent<IUsableItem>() : null;
                if (usable == null) { _net.RequestThrow(_cameraTarget.forward + Vector3.up * 0.15f); }
                else if (usable.BeginUse(_cameraTarget, out Component target, out Vector3 point)) { _net.RequestUse(held, target, point); }
            }
        }

        /// <summary>"Hold E" interactions: begin when E goes down on a hold target, keep alive while it stays down, end when released or the target changes.</summary>
        private void UpdateHold(Interactable focus, PlayerFrame f)
        {
            bool holding = focus != null && focus.IsHold && f.InteractHeld;
            if (!holding || focus != _holdTarget) { EndHold(); }
            if (!holding) { return; }
            if (_holdTarget == null)
            {
                _holdTarget = focus;
                _holdTimer = 0f;
                _net.RequestHold(focus, NetPlayer.HoldBegin);
                return;
            }
            _holdTimer += Time.deltaTime;
            if (_holdTimer >= NetTuning.Instance.HoldKeepAliveInterval)
            {
                _holdTimer = 0f;
                _net.RequestHold(focus, NetPlayer.HoldKeepAlive);
            }
        }

        private void EndHold()
        {
            if (_holdTarget == null) { return; }
            if (_net != null) { _net.RequestHold(_holdTarget, NetPlayer.HoldEnd); }
            _holdTarget = null;
        }

        private void OnDisable()
        {
            if (IsOwner) { EndHold(); InteractionFocus.Set(null); }
        }
    }
}
