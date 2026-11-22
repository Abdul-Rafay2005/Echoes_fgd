using UnityEngine;

namespace Echoes
{
    /// <summary>Explains a locked door when inspected (only while its flag is not set).</summary>
    public class SealedNote : Interactable
    {
        public string flag;
        public string prompt = "Inspect the sealed door";
        [TextArea] public string message = "Sealed.";

        public override string GetPrompt(GameSession s) => prompt;
        public override bool CanInteract(GameSession s) => !s.Progress.HasFlag(flag);

        public override void Interact(GameSession s)
        {
            s.hud.ShowMessage(message);
            if (s.audioManager != null) s.audioManager.Play(s.audioManager.interact, 0.4f);
        }
    }
}
