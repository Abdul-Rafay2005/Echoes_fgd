using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Echoes.Editor
{
    /// <summary>Builds the responsive uGUI canvas (HUD + menus) and wires buttons as persistent listeners.</summary>
    public static partial class ProjectBuilder
    {
        static Font uiFont;
        static readonly Color Ink = new Color(0.17f, 0.11f, 0.05f);
        static readonly Color GoldText = new Color(0.98f, 0.78f, 0.38f);
        static readonly Color PanelDark = new Color(0.05f, 0.08f, 0.1f, 0.72f);

        static Sprite UISprite(string n) => AssetDatabase.LoadAssetAtPath<Sprite>(UIDir + "/" + n + ".png");

        static void BuildUI(Transform root, GameSession session, RunePuzzle rune, out HUDController hud, out MenuController menus)
        {
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("UI", typeof(RectTransform));
            canvasGo.transform.SetParent(root, false);
            canvasGo.layer = 5;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var es = new GameObject("EventSystem");
            es.transform.SetParent(root, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();

            hud = BuildHud(canvasGo.transform, session, rune);
            menus = BuildMenus(canvasGo.transform, session);
        }

        // ---------------------------------------------------------------- primitives

        static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Stretch(string name, Transform parent)
        {
            var rt = Rect(name, parent, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Image Img(RectTransform rt, Color color, Sprite sprite = null, bool raycast = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            img.raycastTarget = raycast;
            img.preserveAspect = sprite != null;
            return img;
        }

        static Text Txt(RectTransform rt, string text, int size, TextAnchor align, Color color, FontStyle style = FontStyle.Normal, bool shadow = true)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = uiFont;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            if (shadow)
            {
                var s = rt.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(0f, 0f, 0f, 0.7f);
                s.effectDistance = new Vector2(2f, -2f);
            }
            return t;
        }

        static Button Btn(Transform parent, string label, float y, UnityAction action)
        {
            var rt = Rect("Button_" + label.Replace(" ", ""), parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(440f, 62f));
            var img = Img(rt, new Color(0.14f, 0.18f, 0.2f, 0.95f), null, true);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.highlightedColor = new Color(0.5f, 0.75f, 0.9f);
            colors.pressedColor = new Color(0.35f, 0.55f, 0.7f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.5f);
            b.colors = colors;
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.6f, 0.45f, 0.2f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            var t = Txt(Stretch("Label", rt), label, 30, TextAnchor.MiddleCenter, GoldText, FontStyle.Bold);
            UnityEventTools.AddPersistentListener(b.onClick, action);
            return b;
        }

        static Slider Sld(Transform parent, string label, float y, float min, float max, UnityAction<float> action)
        {
            var row = Rect("Slider_" + label, parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(560f, 44f));
            Txt(Rect("Label", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(200f, 44f)), label, 26, TextAnchor.MiddleLeft, Ink, FontStyle.Bold, false);
            var sliderRt = Rect("Slider", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(340f, 28f));
            var bg = Img(Stretch("Background", sliderRt), new Color(0.2f, 0.16f, 0.1f, 0.8f), null, true);
            var fillArea = Stretch("Fill Area", sliderRt);
            fillArea.offsetMin = new Vector2(8f, 6f);
            fillArea.offsetMax = new Vector2(-8f, -6f);
            var fill = Stretch("Fill", fillArea);
            Img(fill, new Color(0.25f, 0.62f, 0.9f));
            var handleArea = Stretch("Handle Slide Area", sliderRt);
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);
            var handle = Rect("Handle", handleArea, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 40f));
            var handleImg = Img(handle, GoldText, null, true);
            var s = sliderRt.gameObject.AddComponent<Slider>();
            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = handleImg;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = min;
            s.maxValue = max;
            s.value = (min + max) * 0.5f;
            UnityEventTools.AddPersistentListener(s.onValueChanged, action);
            return s;
        }

        static GameObject Panel(Transform parent, string name, string title, float height = 820f)
        {
            var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(780f, height));
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/parchment.png");
            raw.uvRect = new Rect(0f, 0f, 3f, 3f);
            raw.color = new Color(1f, 0.95f, 0.85f);
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.24f, 0.1f);
            outline.effectDistance = new Vector2(4f, -4f);
            Txt(Rect("Title", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(720f, 80f)), title, 54, TextAnchor.MiddleCenter, Ink, FontStyle.Bold, false);
            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        static Text Body(Transform panel, string text, float y, float height, int size = 26, TextAnchor align = TextAnchor.UpperCenter)
        {
            var t = Txt(Rect("Body", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(680f, height)), text, size, align, Ink, FontStyle.Normal, false);
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        // ---------------------------------------------------------------- HUD

        static HUDController BuildHud(Transform canvas, GameSession session, RunePuzzle rune)
        {
            var hudRoot = Stretch("HUD", canvas);
            var group = hudRoot.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            var hud = canvas.gameObject.AddComponent<HUDController>();
            hud.session = session;
            hud.hudGroup = group;
            hud.runePuzzle = rune;

            // Status (top-left)
            var status = Rect("Status", hudRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(430f, 140f));
            Img(status, PanelDark);
            hud.healthIcons = new Image[3];
            for (int i = 0; i < 3; i++)
                hud.healthIcons[i] = Img(Rect("Health" + i, status, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f + i * 62f, -12f), new Vector2(54f, 54f)), Color.white, UISprite("health"));
            Txt(Rect("HealthLabel", status, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(208f, -12f), new Vector2(210f, 54f)), "HEALTH", 24, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.8f));
            hud.keyIcons = new Image[3];
            string[] glyphs = { "sun", "leaf", "wave" };
            for (int i = 0; i < 3; i++)
                hud.keyIcons[i] = Img(Rect("Key_" + glyphs[i], status, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f + i * 62f, -76f), new Vector2(52f, 52f)), Color.gray, UISprite(glyphs[i]));
            hud.keyCountText = Txt(Rect("KeyCount", status, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(208f, -76f), new Vector2(210f, 52f)), "KEYS 0/3", 28, TextAnchor.MiddleLeft, GoldText, FontStyle.Bold);

            // Score + timeline (top-right)
            var right = Rect("TimelineScore", hudRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(470f, 140f));
            hud.timelinePanel = Img(right, PanelDark);
            hud.scoreText = Txt(Rect("Score", right, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -8f), new Vector2(440f, 44f)), "SCORE 0", 34, TextAnchor.MiddleRight, GoldText, FontStyle.Bold);
            hud.timelineIcon = Img(Rect("TimelineIcon", right, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -54f), new Vector2(44f, 44f)), Color.white, UISprite("present"));
            hud.timelineText = Txt(Rect("TimelineLabel", right, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(66f, -56f), new Vector2(390f, 40f)), "PRESENT", 27, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            hud.qHintText = Txt(Rect("QHint", right, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -100f), new Vector2(440f, 34f)), "[Q]  Shift to the Past", 22, TextAnchor.MiddleLeft, Color.white);
            hud.pastSprite = UISprite("past");
            hud.presentSprite = UISprite("present");

            // Objective (top-centre)
            var obj = Rect("Objective", hudRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(860f, 64f));
            Img(obj, PanelDark);
            hud.objectiveText = Txt(Stretch("Text", obj), "", 25, TextAnchor.MiddleCenter, Color.white);

            // Crosshair and prompt
            hud.crosshair = Img(Rect("Crosshair", hudRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f)), Color.white, UISprite("crosshair"));
            var prompt = Rect("Prompt", hudRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -78f), new Vector2(620f, 52f));
            Img(prompt, PanelDark);
            Img(Rect("Icon", prompt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(40f, 40f)), GoldText, UISprite("interact"));
            hud.promptText = Txt(Rect("Text", prompt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(60f, 0f), new Vector2(550f, 52f)), "", 26, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            hud.promptRoot = prompt.gameObject;
            prompt.gameObject.SetActive(false);

            // Messages
            var msg = Rect("Message", hudRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -210f), new Vector2(1120f, 86f));
            Img(msg, new Color(0.05f, 0.08f, 0.1f, 0.78f));
            hud.messageGroup = msg.gameObject.AddComponent<CanvasGroup>();
            hud.messageGroup.alpha = 0f;
            hud.messageGroup.blocksRaycasts = false;
            hud.messageText = Txt(Stretch("Text", msg), "", 27, TextAnchor.MiddleCenter, Color.white);

            // Rune input
            var runeRt = Rect("RuneInput", hudRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(330f, 120f));
            Img(runeRt, PanelDark);
            Txt(Rect("Label", runeRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(320f, 30f)), "RUNE SEQUENCE", 20, TextAnchor.MiddleCenter, new Color(0.6f, 0.85f, 1f));
            hud.runeSlots = new Image[3];
            for (int i = 0; i < 3; i++)
                hud.runeSlots[i] = Img(Rect("Slot" + i, runeRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2((i - 1) * 95f, 10f), new Vector2(72f, 72f)), new Color(1f, 1f, 1f, 0.15f));
            hud.glyphSprites = new[] { UISprite("sun"), UISprite("leaf"), UISprite("wave") };
            hud.runeRoot = runeRt.gameObject;
            runeRt.gameObject.SetActive(false);

            // Controls reminder
            Txt(Rect("ControlsHint", hudRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 16f), new Vector2(900f, 32f)),
                "WASD move   Mouse look   E interact   Q shift time   Esc pause", 20, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.6f));

            // Effects (above HUD, below menus)
            hud.damageImage = Img(Stretch("DamageFlash", canvas), new Color(0.75f, 0.1f, 0.08f, 0f));
            hud.damageImage.enabled = false;
            hud.fadeImage = Img(Stretch("Fade", canvas), new Color(0f, 0f, 0f, 0f));
            hud.fadeImage.enabled = false;
            return hud;
        }

        // ---------------------------------------------------------------- menus

        static MenuController BuildMenus(Transform canvas, GameSession session)
        {
            var root = Stretch("Menus", canvas);
            var menus = canvas.gameObject.AddComponent<MenuController>();
            menus.session = session;
            var backdrop = Stretch("Backdrop", root);
            Img(backdrop, new Color(0.03f, 0.05f, 0.07f, 0.72f), null, true);
            menus.backdrop = backdrop.gameObject;

            var vol = new System.Collections.Generic.List<Slider>();
            var sens = new System.Collections.Generic.List<Slider>();

            // Main
            var main = Panel(root, "MainMenu", "ECHOES OF ETERNITY", 860f);
            Txt(Rect("Tagline", main.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(700f, 40f)), "Explore  -  Solve  -  Escape", 28, TextAnchor.MiddleCenter, new Color(0.25f, 0.4f, 0.55f), FontStyle.Italic, false);
            Btn(main.transform, "New Game", -180f, menus.OnNewGame);
            menus.continueButton = Btn(main.transform, "Continue", -255f, menus.OnContinue);
            Btn(main.transform, "Controls", -330f, menus.OnControls);
            Btn(main.transform, "Quit", -405f, menus.OnQuit);
            menus.continueNote = Body(main.transform, "", -478f, 60f, 21);
            vol.Add(Sld(main.transform, "Volume", -560f, 0f, 1f, menus.OnVolumeChanged));
            sens.Add(Sld(main.transform, "Mouse", -620f, 0.3f, 6f, menus.OnSensitivityChanged));
            menus.bestScoreText = Body(main.transform, "Best score: 0", -690f, 40f, 24);
            Body(main.transform, "An original procedural low-poly prototype. Built-in render pipeline.", -770f, 40f, 18);
            menus.mainPanel = main;

            // Intro
            var intro = Panel(root, "Intro", "THE SEALED TEMPLE", 820f);
            Body(intro.transform,
                "You slip into an ancient jungle temple and the great door grinds shut behind you.\n\n" +
                "Somewhere inside lie three keys - SUN, LEAF and WAVE - that unlock the chamber of the Heart of Eternity. " +
                "Take the Heart and the entrance will open again.\n\n" +
                "Your hourglass lets you step between the ruined PRESENT and the intact PAST of this place. " +
                "What blocks your path in one time may be open in the other.\n\n" +
                "WASD move   -   Mouse look   -   E interact\nQ shift time   -   Esc pause",
                -130f, 520f, 27);
            Btn(intro.transform, "Begin", -700f, menus.OnBegin);
            menus.introPanel = intro;

            // Pause
            var pause = Panel(root, "Pause", "PAUSED", 760f);
            Btn(pause.transform, "Resume", -150f, menus.OnResume);
            Btn(pause.transform, "Controls", -225f, menus.OnControls);
            Btn(pause.transform, "Main Menu", -300f, menus.OnMainMenu);
            Btn(pause.transform, "Quit", -375f, menus.OnQuit);
            vol.Add(Sld(pause.transform, "Volume", -480f, 0f, 1f, menus.OnVolumeChanged));
            sens.Add(Sld(pause.transform, "Mouse", -540f, 0.3f, 6f, menus.OnSensitivityChanged));
            Body(pause.transform, "Progress is saved at checkpoints and whenever you gain a key or open a door.", -620f, 80f, 21);
            menus.pausePanel = pause;

            // Controls
            var controls = Panel(root, "Controls", "CONTROLS", 760f);
            Body(controls.transform,
                "W A S D  /  Arrows   -   walk\nMouse   -   look around\nE   -   interact (keys, runes, chest, doors)\n" +
                "Q   -   shift between Present and Past\nEsc   -   pause / back\n\n" +
                "Shifting time only works on solid shared stone floor: not while falling, riding the platform or standing on spike plates. " +
                "If something solid would occupy your space in the other time, the shift is refused.",
                -130f, 470f, 25, TextAnchor.UpperLeft);
            Btn(controls.transform, "Back", -640f, menus.OnBack);
            menus.controlsPanel = controls;

            // Lost
            var lost = Panel(root, "Defeat", "YOU HAVE FALLEN", 600f);
            menus.lostStats = Body(lost.transform, "", -140f, 120f, 28);
            Btn(lost.transform, "Retry from Checkpoint", -300f, menus.OnRetry);
            Btn(lost.transform, "Main Menu", -380f, menus.OnMainMenu);
            menus.lostPanel = lost;

            // Won
            var won = Panel(root, "Victory", "YOU ESCAPED!", 640f);
            Body(won.transform, "The Heart of Eternity is yours. Daylight floods the courtyard.", -125f, 60f, 26);
            menus.wonStats = Body(won.transform, "", -200f, 140f, 30);
            Btn(won.transform, "New Game", -390f, menus.OnNewGame);
            Btn(won.transform, "Main Menu", -465f, menus.OnMainMenu);
            menus.wonPanel = won;

            menus.volumeSliders = vol.ToArray();
            menus.sensitivitySliders = sens.ToArray();
            backdrop.gameObject.SetActive(false);
            return menus;
        }
    }
}
