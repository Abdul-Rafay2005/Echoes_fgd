using UnityEngine;

namespace Echoes
{
    /// <summary>Gentle bob and spin for pickups.</summary>
    public class PickupBob : MonoBehaviour
    {
        public float height = 0.06f;
        public float speed = 1.6f;
        public float spin = 40f;
        Vector3 basePos;

        void Awake() => basePos = transform.localPosition;

        void Update()
        {
            transform.localPosition = basePos + Vector3.up * (Mathf.Sin(Time.time * speed) * height);
            transform.Rotate(0f, spin * Time.deltaTime, 0f, Space.Self);
        }
    }
}
