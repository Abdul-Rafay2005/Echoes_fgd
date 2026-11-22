using UnityEngine;

namespace Echoes
{
    /// <summary>Short centre-screen raycast that finds an Interactable and handles the E key.</summary>
    [DefaultExecutionOrder(20)]
    public class PlayerInteractor : MonoBehaviour
    {
        public GameSession session;
        public Camera viewCamera;
        public EchoesTuning tuning;

        public Interactable Current { get; private set; }
        public bool IsBusy => Time.time < busyUntil;
        float busyUntil;

        void Update()
        {
            bool pressed = EchoesInput.InteractPressed; // always consume, even with no target
            Current = null;
            if (session == null || session.State != GameState.Playing || session.IsRespawning)
            {
                if (session != null && session.hud != null) session.hud.SetPrompt(null);
                return;
            }

            Current = FindTarget();
            if (session.hud != null) session.hud.SetPrompt(Current != null ? Current.GetPrompt(session) : null);

            if (pressed && Current != null && !IsBusy)
            {
                busyUntil = Time.time + 0.3f;
                Current.Interact(session);
            }
        }

        public Interactable FindTarget()
        {
            if (viewCamera == null) return null;
            var ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, tuning.interactRange, EchoesLayers.InteractMask, QueryTriggerInteraction.Ignore))
                return null;
            var target = hit.collider.GetComponentInParent<Interactable>();
            if (target == null || !target.isActiveAndEnabled || !target.CanInteract(session)) return null;
            return target;
        }
    }

    /// <summary>
    /// Base for objects that mirror run progress (opened doors, collected keys...). Subscribes on enable
    /// and refreshes instantly, so objects under a re-enabled timeline root never reset progress.
    /// </summary>
    public abstract class ProgressListener : MonoBehaviour
    {
        protected virtual void OnEnable()
        {
            GameSession.ProgressChanged += HandleProgress;
            var s = GameSession.Instance;
            if (s != null) Refresh(s.Progress, true);
        }

        protected virtual void OnDisable() => GameSession.ProgressChanged -= HandleProgress;

        void HandleProgress(RunProgress p, bool instant) => Refresh(p, instant);

        protected virtual void Refresh(RunProgress progress, bool instant) { }
    }

    /// <summary>Base class for anything the player can use with E.</summary>
    public abstract class Interactable : ProgressListener
    {
        public abstract string GetPrompt(GameSession session);
        public virtual bool CanInteract(GameSession session) => true;
        public abstract void Interact(GameSession session);
    }
}
