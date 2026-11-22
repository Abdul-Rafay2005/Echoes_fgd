using UnityEngine;

namespace Echoes
{
    /// <summary>Subtle flicker for torch lights.</summary>
    [RequireComponent(typeof(Light))]
    public class TorchFlicker : MonoBehaviour
    {
        public float amount = 0.18f;
        public float speed = 6f;
        Light lightSource;
        float baseIntensity;
        float seed;

        void Awake()
        {
            lightSource = GetComponent<Light>();
            baseIntensity = lightSource.intensity;
            seed = Random.value * 100f;
        }

        void Update()
        {
            float n = Mathf.PerlinNoise(seed, Time.time * speed);
            lightSource.intensity = baseIntensity * (1f - amount + n * amount * 2f);
        }
    }
}
