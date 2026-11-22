using UnityEngine;
using UnityEngine.UI;

namespace Echoes
{
    /// <summary>Main menu, intro, pause, controls, defeat and victory panels. Buttons are wired by the builder.</summary>
    public class MenuController : MonoBehaviour
    {
        public GameSession session;

        [Header("Panels")]
        public GameObject backdrop;
        public GameObject mainPanel;
        public GameObject introPanel;
        public GameObject pausePanel;
        public GameObject controlsPanel;
        public GameObject lostPanel;
        public GameObject wonPanel;

        [Header("Widgets")]
        public Button continueButton;
        public Text continueNote;
        public Text bestScoreText;
        public Text lostStats;
        public Text wonStats;
        public Slider[] volumeSliders;
        public Slider[] sensitivitySliders;

        GameObject controlsReturn;
        bool syncing;

        GameObject[] All => new[] { mainPanel, introPanel, pausePanel, controlsPanel, lostPanel, wonPanel };

        public GameObject ActivePanel
        {
            get
            {
                foreach (var p in All) if (p != null && p.activeSelf) return p;
                return null;
            }
        }

        void Start() => SyncSliders();

        void Show(GameObject panel)
        {
            foreach (var p in All) if (p != null) p.SetActive(p == panel);
            if (backdrop != null) backdrop.SetActive(panel != null);
            SyncSliders();
        }

        public void HideAll() => Show(null);

        public void ShowMain()
        {
            if (continueButton != null) continueButton.interactable = session.HasValidSave;
            if (continueNote != null) continueNote.text = session.SaveStatus;
            if (bestScoreText != null) bestScoreText.text = "Best score: " + session.BestScore;
            Show(mainPanel);
        }

        public void ShowIntro() => Show(introPanel);
        public void ShowPause() => Show(pausePanel);

        public void ShowLost(int score, int deaths)
        {
            if (lostStats != null) lostStats.text = "Score " + score + "     Deaths " + deaths + "\nKeys, solved puzzles and opened shortcuts are kept.";
            Show(lostPanel);
        }

        public void ShowWon(int score, int deaths, int best)
        {
            if (wonStats != null) wonStats.text = "Final score " + score + "\nDeaths " + deaths + "\nBest score " + best;
            Show(wonPanel);
        }

        public void BackOrResume()
        {
            if (controlsPanel != null && controlsPanel.activeSelf) OnBack();
            else session.Resume();
        }

        void SyncSliders()
        {
            syncing = true;
            float vol = session != null && session.audioManager != null ? session.audioManager.MasterVolume : SaveService.LoadVolume();
            float sens = session != null && session.motor != null ? session.motor.Sensitivity : SaveService.LoadSensitivity(2f);
            if (volumeSliders != null) foreach (var s in volumeSliders) if (s != null) s.SetValueWithoutNotify(vol);
            if (sensitivitySliders != null) foreach (var s in sensitivitySliders) if (s != null) s.SetValueWithoutNotify(sens);
            syncing = false;
        }

        // ---- button handlers (persistent listeners) ----

        public void OnNewGame() => session.RequestNewGame();
        public void OnContinue() { if (session.HasValidSave) session.RequestContinue(); }
        public void OnBegin() => session.BeginPlay();
        public void OnResume() => session.Resume();
        public void OnRetry() => session.Retry();
        public void OnMainMenu() => session.RequestMainMenu();

        public void OnControls()
        {
            controlsReturn = ActivePanel;
            Show(controlsPanel);
        }

        public void OnBack() => Show(controlsReturn != null ? controlsReturn : mainPanel);

        public void OnQuit()
        {
            Application.Quit();
#if UNITY_EDITOR
            Debug.Log("[Echoes] Quit requested (ignored in the Editor).");
#endif
        }

        public void OnVolumeChanged(float value)
        {
            if (syncing) return;
            if (session.audioManager != null) session.audioManager.MasterVolume = value;
            SaveService.SaveVolume(value);
            SyncSliders();
        }

        public void OnSensitivityChanged(float value)
        {
            if (syncing) return;
            if (session.motor != null) session.motor.Sensitivity = value;
            SaveService.SaveSensitivity(value);
            SyncSliders();
        }
    }
}
