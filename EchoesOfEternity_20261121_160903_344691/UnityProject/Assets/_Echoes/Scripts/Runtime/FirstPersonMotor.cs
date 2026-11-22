using UnityEngine;

namespace Echoes
{
    /// <summary>
    /// CharacterController first-person movement: WASD, mouse look with clamped pitch, gravity,
    /// normalised diagonals and moving-platform carrying by displacement (never by parenting).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(0)]
    public class FirstPersonMotor : MonoBehaviour
    {
        public EchoesTuning tuning;
        public Transform cameraPivot;
        public AudioManager audioManager;

        [Tooltip("Set by GameSession: only true while actively playing.")]
        public bool inputEnabled;

        public CharacterController Controller { get; private set; }
        public bool IsGrounded { get; private set; }
        public MovingPlatform CurrentPlatform { get; private set; }
        public Collider GroundCollider { get; private set; }
        public float Pitch => pitch;
        public float Yaw => yaw;
        public Vector3 Velocity { get; private set; }
        public float Sensitivity { get; set; }

        float pitch;
        float yaw;
        float verticalVelocity;
        float stepAccumulator;

        void Awake()
        {
            Controller = GetComponent<CharacterController>();
            yaw = transform.eulerAngles.y;
            Sensitivity = SaveService.LoadSensitivity(tuning != null ? tuning.mouseSensitivity : 2f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 start = transform.position;

            // 1. Carry by platform displacement (collision-checked through CharacterController.Move).
            if (CurrentPlatform != null && CurrentPlatform.FrameDelta != Vector3.zero)
                Controller.Move(CurrentPlatform.FrameDelta);

            // 2. Look.
            if (inputEnabled)
            {
                Vector2 look = EchoesInput.Look * Sensitivity;
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, -tuning.maxPitch, tuning.maxPitch);
            }
            ApplyRotation();

            // 3. Move: clamp the input vector so diagonals are never faster.
            Vector2 input = inputEnabled ? EchoesInput.Move : Vector2.zero;
            Vector3 wish = transform.right * input.x + transform.forward * input.y;
            wish = Vector3.ClampMagnitude(wish, 1f) * tuning.walkSpeed;

            if (IsGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            else verticalVelocity = Mathf.Max(verticalVelocity + tuning.gravity * dt, -40f);

            CollisionFlags flags = Controller.Move((wish + Vector3.up * verticalVelocity) * dt);
            IsGrounded = (flags & CollisionFlags.Below) != 0 || Controller.isGrounded;
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;

            DetectSupport();

            Vector3 moved = transform.position - start;
            Velocity = moved / dt;

            // Footsteps from the player's own walking only.
            if (IsGrounded && wish.sqrMagnitude > 0.01f && audioManager != null)
            {
                stepAccumulator += new Vector3(wish.x, 0f, wish.z).magnitude * dt;
                if (stepAccumulator >= tuning.footstepDistance)
                {
                    stepAccumulator = 0f;
                    audioManager.PlayFootstep();
                }
            }
        }

        void DetectSupport()
        {
            CurrentPlatform = null;
            GroundCollider = null;
            if (!IsGrounded) return;
            float r = Controller.radius * 0.9f;
            Vector3 origin = transform.position + Vector3.up * (Controller.radius + 0.05f);
            if (Physics.SphereCast(origin, r, Vector3.down, out RaycastHit hit, 0.35f, EchoesLayers.GroundMask, QueryTriggerInteraction.Ignore))
            {
                GroundCollider = hit.collider;
                CurrentPlatform = hit.collider.GetComponentInParent<MovingPlatform>();
            }
        }

        void ApplyRotation()
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>Moves the player instantly, clearing velocity and platform support.</summary>
        public void Teleport(Vector3 feetPosition, float yawDegrees)
        {
            Controller.enabled = false;
            transform.position = feetPosition;
            yaw = yawDegrees;
            pitch = 0f;
            ApplyRotation();
            Controller.enabled = true;
            verticalVelocity = 0f;
            Velocity = Vector3.zero;
            CurrentPlatform = null;
            GroundCollider = null;
            IsGrounded = false;
            Physics.SyncTransforms();
        }

        /// <summary>Points the view at a world position (used by tests and cut-ins).</summary>
        public void LookAt(Vector3 worldPoint)
        {
            Vector3 eye = cameraPivot != null ? cameraPivot.position : transform.position + Vector3.up * 1.6f;
            Vector3 d = worldPoint - eye;
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float flat = new Vector2(d.x, d.z).magnitude;
            pitch = Mathf.Clamp(-Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg, -tuning.maxPitch, tuning.maxPitch);
            ApplyRotation();
        }

        /// <summary>World-space capsule of the controller at an optional feet position.</summary>
        public void GetCapsule(Vector3 feet, float inflate, out Vector3 top, out Vector3 bottom, out float radius)
        {
            var cc = Controller != null ? Controller : GetComponent<CharacterController>();
            radius = cc.radius + inflate;
            Vector3 center = feet + cc.center;
            float half = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
            top = center + Vector3.up * half;
            bottom = center - Vector3.up * half;
        }
    }
}
