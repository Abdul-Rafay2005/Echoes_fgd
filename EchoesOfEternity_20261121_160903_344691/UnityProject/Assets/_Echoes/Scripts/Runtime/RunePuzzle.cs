using System.Collections.Generic;
using UnityEngine;

namespace Echoes
{
    /// <summary>
    /// Rune Gallery controller (shared root). The Past clue wall shows SUN, WAVE, LEAF; the Present
    /// rune controls forward presses here. Wrong input resets only the unfinished sequence.
    /// Solving sets a permanent flag that opens the key cache.
    /// </summary>
    public class RunePuzzle : ProgressListener
    {
        public GameSession session;
        public string puzzleId = EchoesIds.PuzzleRune;
        public KeyId[] solution = { KeyId.Sun, KeyId.Wave, KeyId.Leaf };
        public RuneControl[] controls;

        readonly List<KeyId> input = new List<KeyId>();
        public IReadOnlyList<KeyId> CurrentInput => input;
        public bool Solved => session != null && session.Progress.HasFlag(puzzleId);

        public void Press(KeyId glyph)
        {
            if (session == null || Solved) return;
            input.Add(glyph);
            int i = input.Count - 1;
            var audio = session.audioManager;
            if (input[i] != solution[i])
            {
                input.Clear();
                if (audio != null) audio.Play(audio.interact, 0.35f);
                session.hud.ShowMessage("The runes dim softly. The sequence fades - try again.", 2.4f);
            }
            else if (input.Count == solution.Length)
            {
                input.Clear();
                session.SolvePuzzle(puzzleId, "The runes blaze in order. A cache grinds open!");
            }
            else
            {
                if (audio != null) audio.Play(audio.interact, 0.6f);
            }
            UpdateVisuals();
        }

        public void ResetPartial()
        {
            input.Clear();
            UpdateVisuals();
        }

        protected override void Refresh(RunProgress progress, bool instant) => UpdateVisuals();

        void UpdateVisuals()
        {
            if (controls == null) return;
            bool solved = Solved;
            foreach (var c in controls)
                if (c != null) c.SetLit(solved, input.Contains(c.glyph));
        }
    }
}
