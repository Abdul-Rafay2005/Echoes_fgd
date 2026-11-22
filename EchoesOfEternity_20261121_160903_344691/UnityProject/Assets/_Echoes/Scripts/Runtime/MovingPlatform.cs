using UnityEngine;

namespace Echoes
{
    /// <summary>
    /// Kinematic platform between two endpoints with dwell time. Runs before the player motor and
    /// exposes FrameDelta so the CharacterController is carried by collision-checked displacement.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class MovingPlatform : MonoBehaviour
    {
        public enum Mode { DwellA, ToB, DwellB, ToA }

        public EchoesTuning tuning;
        public AudioManager audioManager;
        public Transform endA;
        public Transform endB;

        public Vector3 FrameDelta { get; private set; }
        public Mode CurrentMode => mode;
        public bool IsMoving => mode == Mode.ToA || mode == Mode.ToB;
        public bool IsDockedAtA => mode == Mode.DwellA;
        public bool IsDockedAtB => mode == Mode.DwellB;
        public float DwellRemaining => (mode == Mode.DwellA || mode == Mode.DwellB) ? tuning.platformDwell - timer : 0f;

        Mode mode = Mode.DwellA;
        float timer;
        float progress; // 0 = A, 1 = B

        void Start() => ResetToSafeEndpoint();

        void Update()
        {
            FrameDelta = Vector3.zero;
            float dt = Time.deltaTime;
            if (dt <= 0f || endA == null || endB == null) return;

            float distance = Vector3.Distance(endA.position, endB.position);
            float duration = Mathf.Max(0.1f, distance / tuning.platformSpeed);
            switch (mode)
            {
                case Mode.DwellA:
                    timer += dt;
                    if (timer >= tuning.platformDwell) Depart(Mode.ToB);
                    break;
                case Mode.DwellB:
                    timer += dt;
                    if (timer >= tuning.platformDwell) Depart(Mode.ToA);
                    break;
                case Mode.ToB:
                    progress = Mathf.MoveTowards(progress, 1f, dt / duration);
                    if (progress >= 1f) { mode = Mode.DwellB; timer = 0f; }
                    break;
                case Mode.ToA:
                    progress = Mathf.MoveTowards(progress, 0f, dt / duration);
                    if (progress <= 0f) { mode = Mode.DwellA; timer = 0f; }
                    break;
            }

            Vector3 before = transform.position;
            Vector3 target = Vector3.Lerp(endA.position, endB.position, Mathf.SmoothStep(0f, 1f, progress));
            if (target != before)
            {
                transform.position = target;
                FrameDelta = target - before;
                Physics.SyncTransforms();
            }
        }

        void Depart(Mode next)
        {
            mode = next;
            timer = 0f;
            if (audioManager != null) audioManager.PlayAt(audioManager.platformMove, transform.position, 0.5f);
        }

        /// <summary>Puts the platform at endpoint A, docked and stationary.</summary>
        public void ResetToSafeEndpoint()
        {
            mode = Mode.DwellA;
            timer = 0f;
            progress = 0f;
            FrameDelta = Vector3.zero;
            if (endA != null) transform.position = endA.position;
            Physics.SyncTransforms();
        }
    }
}
