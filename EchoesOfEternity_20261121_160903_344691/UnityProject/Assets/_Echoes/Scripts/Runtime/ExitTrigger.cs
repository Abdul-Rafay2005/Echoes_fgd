using UnityEngine;

namespace Echoes
{
    /// <summary>Win volume beyond the entrance door. Only counts with the Heart owned.</summary>
    public class ExitTrigger : MonoBehaviour
    {
        float nextMessage;

        public void PlayerInside(GameSession session)
        {
            if (session.Progress.heartOwned)
            {
                session.Win();
            }
            else if (Time.time >= nextMessage)
            {
                nextMessage = Time.time + 3f;
                if (session.hud != null) session.hud.ShowMessage("The Heart of Eternity is still inside the temple.");
            }
        }
    }
}
