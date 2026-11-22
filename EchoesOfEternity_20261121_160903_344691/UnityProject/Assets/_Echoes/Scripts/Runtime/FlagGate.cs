using System.Collections;
using UnityEngine;

namespace Echoes
{
    /// <summary>
    /// A door or cache panel that is open exactly when a permanent progress flag is set.
    /// Opening sinks the panel into the floor and disables its blockers. It never re-closes during a run.
    /// </summary>
    public class FlagGate : ProgressListener
    {
        [Tooltip("Progress flag (puzzle, door/shortcut id, or 'heart').")] public string flag;
        public Transform panel;
        public Collider[] blockers;
        public Vector3 openOffset = new Vector3(0f, -2.95f, 0f);
        public float duration = 1.3f;
        public AudioManager audioManager;

        public bool IsOpen { get; private set; }
        public bool IsAnimating => anim != null;

        Vector3 closedLocal;
        bool initialised;
        Coroutine anim;

        void Awake() => Init();

        void Init()
        {
            if (initialised || panel == null) return;
            closedLocal = panel.localPosition;
            initialised = true;
        }

        protected override void Refresh(RunProgress progress, bool instant)
        {
            Init();
            bool open = progress.HasFlag(flag);
            if (open == IsOpen && !instant) return;
            SetOpen(open, instant);
        }

        void SetOpen(bool open, bool instant)
        {
            bool changed = open != IsOpen;
            IsOpen = open;
            if (blockers != null) foreach (var b in blockers) if (b != null) b.enabled = !open;
            if (anim != null) { StopCoroutine(anim); anim = null; }
            Vector3 target = open ? closedLocal + openOffset : closedLocal;
            if (panel == null) return;
            if (instant || !isActiveAndEnabled)
            {
                panel.localPosition = target;
            }
            else
            {
                if (changed && audioManager != null) audioManager.PlayAt(audioManager.doorOpen, panel.position + Vector3.up, 0.7f);
                anim = StartCoroutine(Move(target));
            }
        }

        /// <summary>Intro beat: the panel starts open and slams shut behind the explorer.</summary>
        public void PlaySeal()
        {
            Init();
            if (IsOpen || panel == null) return;
            if (anim != null) StopCoroutine(anim);
            panel.localPosition = closedLocal + openOffset;
            if (audioManager != null) audioManager.PlayAt(audioManager.doorOpen, panel.position + Vector3.up * 2f, 0.9f);
            anim = StartCoroutine(Move(closedLocal, 0.7f));
        }

        IEnumerator Move(Vector3 target, float time = -1f)
        {
            float d = time > 0f ? time : duration;
            Vector3 start = panel.localPosition;
            for (float t = 0f; t < d; t += Time.deltaTime)
            {
                panel.localPosition = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / d));
                yield return null;
            }
            panel.localPosition = target;
            anim = null;
        }
    }
}
