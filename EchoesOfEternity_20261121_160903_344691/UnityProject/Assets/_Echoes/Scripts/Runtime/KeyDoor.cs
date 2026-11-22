using UnityEngine;

namespace Echoes
{
    /// <summary>
    /// The Heart chamber door. Rejects 0-2 keys; with all three it sets a permanent flag (keys are not
    /// consumed). The matching FlagGate performs the actual opening.
    /// </summary>
    public class KeyDoor : Interactable
    {
        public string doorId = EchoesIds.DoorHeart;
        public int requiredKeys = 3;
        [Tooltip("Glyph slots on the door in KeyId order: Sun, Leaf, Wave.")] public SpriteRenderer[] keySlots;
        public Color slotOff = new Color(0.25f, 0.27f, 0.3f, 0.9f);

        public override string GetPrompt(GameSession s)
        {
            int n = s.Progress.KeyCount;
            return n >= requiredKeys ? "Unlock the Heart chamber" : "Inspect the Heart door (" + n + "/" + requiredKeys + " keys)";
        }

        public override bool CanInteract(GameSession s) => !s.Progress.HasFlag(doorId);

        public override void Interact(GameSession s)
        {
            if (!CanInteract(s)) return;
            if (s.Progress.KeyCount < requiredKeys)
            {
                s.hud.ShowMessage("Three glyph locks: Sun, Leaf and Wave. You hold " + s.Progress.KeyCount + " of " + requiredKeys + " keys.");
                if (s.audioManager != null) s.audioManager.Play(s.audioManager.interact, 0.5f);
                return;
            }
            s.SetDoorFlag(doorId, "The three keys turn. The Heart chamber opens.");
        }

        protected override void Refresh(RunProgress progress, bool instant)
        {
            if (keySlots == null) return;
            for (int i = 0; i < keySlots.Length && i < 3; i++)
            {
                if (keySlots[i] == null) continue;
                var k = (KeyId)i;
                keySlots[i].color = progress.HasKey(k) ? EchoesIds.KeyColor(k) * 1.2f : slotOff;
            }
        }
    }
}
