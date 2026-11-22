using UnityEngine;

namespace Echoes
{
    /// <summary>
    /// Records which timeline an always-enabled query proxy represents, and the source blocker it mirrors.
    /// The proxies sit on EE_QueryPast / EE_QueryPresent, which never collide physically.
    /// </summary>
    public class TimelineBlockerProxy : MonoBehaviour
    {
        public Timeline timeline;
        public Collider source;
    }
}
