using UnityEngine;

namespace Echoes
{
    /// <summary>Checkpoint on common stationary floor. A child trigger on EE_Zones activates it.</summary>
    public class Checkpoint : MonoBehaviour
    {
        public string id = "hub";
        [Tooltip("Timeline restored when respawning here (validated before use).")]
        public Timeline restoreTimeline = Timeline.Present;
        public Transform spawnPoint;
        public Light markerLight;

        public Vector3 SpawnPosition => spawnPoint != null ? spawnPoint.position : transform.position;
        public float SpawnYaw => spawnPoint != null ? spawnPoint.eulerAngles.y : transform.eulerAngles.y;

        public void SetCurrent(bool current)
        {
            if (markerLight != null) markerLight.intensity = current ? 2.4f : 0.7f;
        }
    }
}
