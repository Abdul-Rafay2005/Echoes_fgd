using UnityEngine;

namespace Echoes
{
    /// <summary>
    /// Spike plate cycle: Safe -> Warning (visible red glow, spikes peek) -> Active (damage) -> Recovery.
    /// Timing comes from EchoesTuning. Uses scaled time, so pausing freezes it.
    /// </summary>
    public class TimedSpikeTrap : MonoBehaviour
    {
        public enum Phase { Safe, Warning, Active, Recovery }

        public EchoesTuning tuning;
        public AudioManager audioManager;
        [Tooltip("Offset (s) so neighbouring traps are staggered.")] public float startDelay;
        public Transform[] spikes;
        public Renderer[] warningRenderers;
        public Light warningLight;
        public float hiddenY = -0.86f;
        public float warningY = -0.62f;
        public float activeY = 0f;
        public Color warningEmission = new Color(1.6f, 0.25f, 0.15f);

        public Phase CurrentPhase { get; private set; } = Phase.Safe;
        public float Timer => timer;
        public bool IsDamaging => CurrentPhase == Phase.Active;

        float timer;
        MaterialPropertyBlock block;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        void Awake() => block = new MaterialPropertyBlock();

        void Start() => Normalize(0f);

        void Update()
        {
            timer += Time.deltaTime;
            Evaluate();
        }

        /// <summary>Restarts the cycle at the beginning of Safe with optional extra safe time.</summary>
        public void Normalize(float extraSafe)
        {
            timer = -(startDelay + Mathf.Max(0f, extraSafe));
            Evaluate();
        }

        void Evaluate()
        {
            float safe = tuning.trapSafe, warn = tuning.trapWarning, act = tuning.trapActive, rec = tuning.trapRecovery;
            float cycle = safe + warn + act + rec;
            Phase previous = CurrentPhase;
            float y;
            float glow;
            if (timer < 0f)
            {
                CurrentPhase = Phase.Safe; y = hiddenY; glow = 0f;
            }
            else
            {
                float t = timer % cycle;
                if (t < safe) { CurrentPhase = Phase.Safe; y = hiddenY; glow = 0f; }
                else if (t < safe + warn)
                {
                    CurrentPhase = Phase.Warning;
                    float k = (t - safe) / warn;
                    y = Mathf.Lerp(hiddenY, warningY, Mathf.Clamp01(k * 3f)) + Mathf.Sin(t * 40f) * 0.01f;
                    glow = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(k * Mathf.PI * 3f));
                }
                else if (t < safe + warn + act)
                {
                    CurrentPhase = Phase.Active;
                    float k = (t - safe - warn) / 0.08f;
                    y = Mathf.Lerp(warningY, activeY, Mathf.Clamp01(k));
                    glow = 1f;
                }
                else
                {
                    CurrentPhase = Phase.Recovery;
                    float k = (t - safe - warn - act) / rec;
                    y = Mathf.Lerp(activeY, hiddenY, Mathf.Clamp01(k));
                    glow = 0.3f * (1f - k);
                }
            }

            if (spikes != null)
                foreach (var s in spikes)
                    if (s != null) s.localPosition = new Vector3(s.localPosition.x, y, s.localPosition.z);

            if (warningRenderers != null && block != null)
            {
                block.SetColor(EmissionId, warningEmission * glow);
                foreach (var r in warningRenderers) if (r != null) r.SetPropertyBlock(block);
            }
            if (warningLight != null) warningLight.intensity = glow * 2.2f;

            if (previous != CurrentPhase && CurrentPhase == Phase.Warning && audioManager != null)
                audioManager.PlayAt(audioManager.spikeWarning, transform.position + Vector3.up, 0.45f);
        }
    }
}
