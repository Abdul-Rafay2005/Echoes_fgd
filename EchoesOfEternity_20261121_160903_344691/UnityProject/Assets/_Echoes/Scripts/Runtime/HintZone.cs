using UnityEngine;

namespace Echoes
{
    /// <summary>Shows a one-off hint the first time the player enters the volume in this session.</summary>
    public class HintZone : MonoBehaviour
    {
        [TextArea] public string message;
        public float duration = 5f;
        bool shown;

        public void Trigger(GameSession session)
        {
            if (shown || session == null || session.hud == null) return;
            shown = true;
            session.hud.ShowMessage(message, duration);
        }
    }
}
