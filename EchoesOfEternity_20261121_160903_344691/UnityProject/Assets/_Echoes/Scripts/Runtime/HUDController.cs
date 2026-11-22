using UnityEngine;
using UnityEngine.UI;

namespace Echoes
{
    /// <summary>
    /// In-game HUD: health icons, key slots (0/3), score, objective, timeline label with Q hint,
    /// crosshair, E prompt, short messages, rune input slots, fade and damage flash.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        public GameSession session;
        public CanvasGroup hudGroup;

        [Header("Status")]
        public Image[] healthIcons;
        public Image[] keyIcons;          // KeyId order: Sun, Leaf, Wave
        public Text keyCountText;
        public Text scoreText;
        public Text objectiveText;

        [Header("Timeline")]
        public Image timelineIcon;
        public Text timelineText;
        public Text qHintText;
        public Image timelinePanel;
        public Sprite pastSprite;
        public Sprite presentSprite;

        [Header("Interaction")]
        public Image crosshair;
        public GameObject promptRoot;
        public Text promptText;

        [Header("Messages and effects")]
        public CanvasGroup messageGroup;
        public Text messageText;
        public Image fadeImage;
        public Image damageImage;

        [Header("Rune input")]
        public GameObject runeRoot;
        public Image[] runeSlots;
        public Sprite[] glyphSprites;     // KeyId order
        public RunePuzzle runePuzzle;

        float messageUntil;
        float damageAlpha;
        float qHintTimer;
        static readonly Color PastColor = new Color(0.55f, 0.82f, 1f);
        static readonly Color PresentColor = new Color(0.72f, 0.86f, 0.45f);

        public void SetVisible(bool visible)
        {
            if (hudGroup != null) hudGroup.alpha = visible ? 1f : 0f;
        }

        public void ShowMessage(string message, float duration = 2.8f)
        {
            if (messageText == null) return;
            messageText.text = message;
            messageUntil = Time.unscaledTime + duration;
        }

        public string CurrentMessage => messageText != null && Time.unscaledTime < messageUntil ? messageText.text : "";

        public void SetPrompt(string prompt)
        {
            if (promptRoot == null) return;
            bool show = !string.IsNullOrEmpty(prompt);
            if (promptRoot.activeSelf != show) promptRoot.SetActive(show);
            if (show && promptText != null) promptText.text = "[E]  " + prompt;
            if (crosshair != null) crosshair.color = show ? new Color(1f, 0.85f, 0.45f) : new Color(1f, 1f, 1f, 0.75f);
        }

        public void SetFade(float alpha, Color color)
        {
            if (fadeImage == null) return;
            color.a = Mathf.Clamp01(alpha);
            fadeImage.color = color;
            fadeImage.enabled = alpha > 0.001f;
        }

        public void FlashDamage() => damageAlpha = 0.55f;

        public void SetTimeline(Timeline t)
        {
            bool past = t == Timeline.Past;
            if (timelineIcon != null) timelineIcon.sprite = past ? pastSprite : presentSprite;
            if (timelineText != null)
            {
                timelineText.text = past ? "PAST  - intact temple" : "PRESENT  - ruined temple";
                timelineText.color = past ? PastColor : PresentColor;
            }
            if (timelinePanel != null) timelinePanel.color = past ? new Color(0.08f, 0.2f, 0.32f, 0.75f) : new Color(0.12f, 0.18f, 0.08f, 0.75f);
        }

        public void RefreshAll()
        {
            if (session == null) return;
            var p = session.Progress;
            if (healthIcons != null)
                for (int i = 0; i < healthIcons.Length; i++)
                    if (healthIcons[i] != null)
                        healthIcons[i].color = i < session.Health ? new Color(0.9f, 0.3f, 0.25f) : new Color(0.2f, 0.2f, 0.2f, 0.45f);
            if (keyIcons != null)
                for (int i = 0; i < keyIcons.Length && i < 3; i++)
                    if (keyIcons[i] != null)
                    {
                        var k = (KeyId)i;
                        keyIcons[i].color = p.HasKey(k) ? EchoesIds.KeyColor(k) : new Color(0.35f, 0.35f, 0.35f, 0.35f);
                    }
            if (keyCountText != null) keyCountText.text = "KEYS " + p.KeyCount + "/3";
            if (scoreText != null) scoreText.text = "SCORE " + p.score;
            if (objectiveText != null) objectiveText.text = session.GetObjective();
        }

        void Update()
        {
            if (session == null) return;
            RefreshAll();

            if (messageGroup != null)
            {
                float remaining = messageUntil - Time.unscaledTime;
                messageGroup.alpha = Mathf.Clamp01(remaining / 0.4f);
            }

            if (damageImage != null)
            {
                damageAlpha = Mathf.MoveTowards(damageAlpha, 0f, Time.unscaledDeltaTime * 1.2f);
                damageImage.color = new Color(0.75f, 0.1f, 0.08f, damageAlpha);
                damageImage.enabled = damageAlpha > 0.001f;
            }

            // Q hint: refreshed a few times per second so players know when shifting is possible.
            qHintTimer -= Time.unscaledDeltaTime;
            if (qHintText != null && qHintTimer <= 0f && session.timeline != null)
            {
                qHintTimer = 0.15f;
                string target = session.timeline.Current == Timeline.Past ? "Present" : "Past";
                if (session.State == GameState.Playing && session.timeline.CanSwitch(out string reason))
                {
                    qHintText.text = "[Q]  Shift to the " + target;
                    qHintText.color = Color.white;
                }
                else
                {
                    session.timeline.CanSwitch(out reason);
                    qHintText.text = "[Q]  " + (session.State == GameState.Transitioning ? "Shifting..." : reason ?? "Unavailable");
                    qHintText.color = new Color(1f, 1f, 1f, 0.45f);
                }
            }

            if (runeRoot != null && runePuzzle != null)
            {
                var input = runePuzzle.CurrentInput;
                bool show = input.Count > 0 && !runePuzzle.Solved;
                if (runeRoot.activeSelf != show) runeRoot.SetActive(show);
                if (show && runeSlots != null)
                {
                    for (int i = 0; i < runeSlots.Length; i++)
                    {
                        if (runeSlots[i] == null) continue;
                        if (i < input.Count)
                        {
                            runeSlots[i].sprite = glyphSprites[(int)input[i]];
                            runeSlots[i].color = new Color(0.55f, 0.85f, 1f);
                        }
                        else
                        {
                            runeSlots[i].sprite = null;
                            runeSlots[i].color = new Color(1f, 1f, 1f, 0.15f);
                        }
                    }
                }
            }
        }
    }
}
