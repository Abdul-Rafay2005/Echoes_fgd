using System.Collections.Generic;
using UnityEngine;

namespace Echoes
{
    /// <summary>
    /// Central audio: quiet looping ambience, 2D UI/feedback sounds and a small pool of positional
    /// sources for mechanisms. Repeated plays of the same clip are throttled so sounds never stack loudly.
    /// Master volume is AudioListener.volume and is stored in settings.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public EchoesTuning tuning;
        public AudioSource ambienceSource;
        public AudioSource uiSource;
        public int poolSize = 8;

        [Header("Clips")]
        public AudioClip ambience;
        public AudioClip damage;
        public AudioClip defeat;
        public AudioClip doorOpen;
        public AudioClip footstep;
        public AudioClip heartPickup;
        public AudioClip interact;
        public AudioClip keyPickup;
        public AudioClip platformMove;
        public AudioClip runeSolved;
        public AudioClip spikeWarning;
        public AudioClip timelineSwitch;
        public AudioClip victory;

        AudioSource[] pool;
        int next;
        readonly Dictionary<AudioClip, float> lastPlayed = new Dictionary<AudioClip, float>();

        public float MasterVolume
        {
            get => AudioListener.volume;
            set => AudioListener.volume = Mathf.Clamp01(value);
        }

        void Awake()
        {
            MasterVolume = SaveService.LoadVolume();
            pool = new AudioSource[Mathf.Max(1, poolSize)];
            for (int i = 0; i < pool.Length; i++)
            {
                var go = new GameObject("PositionalSource" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 2f;
                s.maxDistance = 22f;
                s.dopplerLevel = 0f;
                pool[i] = s;
            }
            if (uiSource != null) uiSource.ignoreListenerPause = true;
            if (ambienceSource != null)
            {
                ambienceSource.ignoreListenerPause = true;
                ambienceSource.clip = ambience;
                ambienceSource.loop = true;
                ambienceSource.spatialBlend = 0f;
                ambienceSource.volume = tuning.ambienceVolume;
                if (ambience != null) ambienceSource.Play();
            }
        }

        bool Throttled(AudioClip clip, float minGap)
        {
            if (clip == null) return true;
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(clip, out float last) && now - last < minGap) return true;
            lastPlayed[clip] = now;
            return false;
        }

        public void Play(AudioClip clip, float volume = 1f)
        {
            if (uiSource == null || Throttled(clip, 0.08f)) return;
            uiSource.PlayOneShot(clip, Mathf.Clamp01(volume * tuning.sfxVolume));
        }

        public void PlayAt(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (pool == null || Throttled(clip, 0.1f)) return;
            var s = pool[next];
            next = (next + 1) % pool.Length;
            s.transform.position = position;
            s.clip = clip;
            s.volume = Mathf.Clamp01(volume * tuning.sfxVolume);
            s.Play();
        }

        public void PlayFootstep() => Play(footstep, 0.25f);
    }
}
