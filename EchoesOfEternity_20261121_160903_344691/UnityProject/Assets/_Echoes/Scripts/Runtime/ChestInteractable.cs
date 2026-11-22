using System.Collections;
using UnityEngine;

namespace Echoes
{
    /// <summary>Treasure chest: opens once with E, awards its key once and opens a permanent shortcut.</summary>
    public class ChestInteractable : Interactable
    {
        public string puzzleId = EchoesIds.PuzzleChest;
        public KeyId reward = KeyId.Wave;
        public string grantDoor = EchoesIds.ShortcutWave;
        public Transform lid;
        public float openAngle = -95f;
        public float openTime = 0.8f;
        public GameObject rewardVisual;

        public bool IsOpen { get; private set; }

        public override string GetPrompt(GameSession s) => "Open the chest";
        public override bool CanInteract(GameSession s) => !s.Progress.HasFlag(puzzleId);

        public override void Interact(GameSession s)
        {
            if (!CanInteract(s)) return;
            s.OpenChest(puzzleId, reward, grantDoor);
        }

        protected override void Refresh(RunProgress progress, bool instant)
        {
            bool open = progress.HasFlag(puzzleId);
            if (open == IsOpen && !instant) return;
            IsOpen = open;
            StopAllCoroutines();
            if (!open || instant || !isActiveAndEnabled)
            {
                if (lid != null) lid.localRotation = Quaternion.Euler(open ? openAngle : 0f, 0f, 0f);
                if (rewardVisual != null) rewardVisual.SetActive(false);
            }
            else
            {
                StartCoroutine(OpenRoutine());
            }
        }

        IEnumerator OpenRoutine()
        {
            if (rewardVisual != null) rewardVisual.SetActive(true);
            Vector3 keyStart = rewardVisual != null ? rewardVisual.transform.localPosition : Vector3.zero;
            for (float t = 0f; t < openTime; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / openTime);
                if (lid != null) lid.localRotation = Quaternion.Euler(openAngle * k, 0f, 0f);
                if (rewardVisual != null) rewardVisual.transform.localPosition = keyStart + Vector3.up * (0.7f * k);
                yield return null;
            }
            if (lid != null) lid.localRotation = Quaternion.Euler(openAngle, 0f, 0f);
            yield return new WaitForSeconds(0.6f);
            if (rewardVisual != null)
            {
                rewardVisual.SetActive(false);
                rewardVisual.transform.localPosition = keyStart;
            }
        }
    }
}
