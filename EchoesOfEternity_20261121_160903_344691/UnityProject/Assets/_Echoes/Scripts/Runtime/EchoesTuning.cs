using UnityEngine;

namespace Echoes
{
    /// <summary>
    /// All gameplay tuning values in one asset (Assets/_Echoes/Settings/EchoesTuning.asset).
    /// Edit it in the Inspector, even during Play Mode. The project builder never overwrites it.
    /// </summary>
    [CreateAssetMenu(menuName = "Echoes/Tuning", fileName = "EchoesTuning")]
    public class EchoesTuning : ScriptableObject
    {
        [Header("Movement")]
        [Tooltip("Walking speed in metres per second.")] public float walkSpeed = 4.2f;
        public float gravity = -20f;
        [Tooltip("Default mouse sensitivity; players can change it in the menu.")] public float mouseSensitivity = 2f;
        [Range(30f, 89f)] public float maxPitch = 85f;
        [Tooltip("Distance between footstep sounds (m).")] public float footstepDistance = 2.1f;

        [Header("Interaction")]
        public float interactRange = 2.2f;

        [Header("Health and damage")]
        public int maxHealth = 3;
        [Tooltip("Seconds of invulnerability after any hit (minimum 1).")] public float damageImmunity = 1.25f;
        public int spikeDamage = 1;
        public int fallDamage = 1;
        [Tooltip("Falling below this height triggers the fall recovery flow.")] public float fallRespawnY = -4f;

        [Header("Spike trap cycle (seconds)")]
        public float trapSafe = 1.7f;
        public float trapWarning = 0.9f;
        public float trapActive = 1.0f;
        public float trapRecovery = 0.6f;
        [Tooltip("Extra safe time given to every trap after respawn or load.")] public float trapRespawnGrace = 1.5f;

        [Header("Moving platform")]
        [Tooltip("Platform speed in metres per second.")] public float platformSpeed = 1.6f;
        [Tooltip("Seconds the platform waits at each landing.")] public float platformDwell = 1.8f;

        [Header("Timeline")]
        public float timelineFade = 0.4f;
        public float timelineCooldown = 0.75f;

        [Header("Score")]
        public int keyScore = 100;
        public int heartScore = 300;
        public int escapeScore = 500;

        [Header("Audio")]
        [Range(0f, 1f)] public float ambienceVolume = 0.22f;
        [Range(0f, 1f)] public float sfxVolume = 0.7f;

        public float TrapCycle => trapSafe + trapWarning + trapActive + trapRecovery;

        void OnValidate()
        {
            damageImmunity = Mathf.Max(1f, damageImmunity);
            walkSpeed = Mathf.Max(0.5f, walkSpeed);
            platformSpeed = Mathf.Max(0.2f, platformSpeed);
            trapSafe = Mathf.Max(0.2f, trapSafe);
            trapWarning = Mathf.Max(0.2f, trapWarning);
            trapActive = Mathf.Max(0.1f, trapActive);
            trapRecovery = Mathf.Max(0.1f, trapRecovery);
            maxHealth = Mathf.Max(1, maxHealth);
        }
    }
}
