using UnityEngine;

namespace Echoes
{
    /// <summary>A key on a pedestal. Exists once in the shared root; its state comes from RunProgress.</summary>
    public class KeyPickup : Interactable
    {
        public KeyId key;
        [Tooltip("Optional flag that must be set before the key can be taken.")] public string requiredFlag;
        [Tooltip("Optional permanent door/shortcut opened when the key is taken.")] public string grantDoor;
        public GameObject visual;
        public Collider[] pickupColliders;
        public Light glow;

        public override string GetPrompt(GameSession s) => "Take the " + EchoesIds.KeyTitle(key) + " key";

        public override bool CanInteract(GameSession s) =>
            !s.Progress.HasKey(key) && s.Progress.HasFlag(requiredFlag);

        public override void Interact(GameSession s)
        {
            if (!CanInteract(s)) return;
            s.CollectKey(key, grantDoor);
        }

        protected override void Refresh(RunProgress progress, bool instant)
        {
            bool has = progress.HasKey(key);
            if (visual != null) visual.SetActive(!has);
            if (glow != null) glow.enabled = !has;
            if (pickupColliders != null) foreach (var c in pickupColliders) if (c != null) c.enabled = !has;
        }
    }
}
