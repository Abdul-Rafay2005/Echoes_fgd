using System.Collections.Generic;
using UnityEngine;

namespace Echoes
{
    /// <summary>
    /// Thin input wrapper over the project's active input handler (legacy Input Manager).
    /// The automated test harness can switch to simulated input; real devices are then ignored.
    /// </summary>
    public static class EchoesInput
    {
        public static bool Simulated;
        public static Vector2 SimMove;
        public static Vector2 SimLook;
        static readonly HashSet<KeyCode> simPressed = new HashSet<KeyCode>();

        public static void SimPress(KeyCode key) => simPressed.Add(key);

        public static void ResetSimulation()
        {
            SimMove = Vector2.zero;
            SimLook = Vector2.zero;
            simPressed.Clear();
        }

        static bool Consume(KeyCode key) => simPressed.Remove(key);

        public static Vector2 Move
        {
            get
            {
                if (Simulated) return SimMove;
                float x = 0f, y = 0f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;
                return new Vector2(x, y);
            }
        }

        /// <summary>Mouse delta for this frame (already frame-based; do not multiply by deltaTime).</summary>
        public static Vector2 Look
        {
            get
            {
                if (Simulated) return SimLook;
                return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            }
        }

        public static bool InteractPressed => Simulated ? Consume(KeyCode.E) : Input.GetKeyDown(KeyCode.E);
        public static bool TimelinePressed => Simulated ? Consume(KeyCode.Q) : Input.GetKeyDown(KeyCode.Q);
        public static bool PausePressed => Simulated ? Consume(KeyCode.Escape) : Input.GetKeyDown(KeyCode.Escape);
    }
}
