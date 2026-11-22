using UnityEngine;

namespace Echoes
{
    /// <summary>The Heart of Eternity. Awards once and permanently unlocks the entrance exit.</summary>
    public class HeartPickup : Interactable
    {
        public GameObject visual;
        public Collider[] pickupColliders;
        public Light glow;

        public override string GetPrompt(GameSession s) => "Take the Heart of Eternity";

        public override bool CanInteract(GameSession s) =>
            !s.Progress.heartOwned && s.Progress.HasFlag(EchoesIds.DoorHeart);

        public override void Interact(GameSession s)
        {
            if (!CanInteract(s)) return;
            s.CollectHeart();
        }

        protected override void Refresh(RunProgress progress, bool instant)
        {
            bool owned = progress.heartOwned;
            if (visual != null) visual.SetActive(!owned);
            if (glow != null) glow.enabled = !owned;
            if (pickupColliders != null) foreach (var c in pickupColliders) if (c != null) c.enabled = !owned;
        }
    }
}
