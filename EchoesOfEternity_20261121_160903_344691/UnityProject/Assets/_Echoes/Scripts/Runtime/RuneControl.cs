using UnityEngine;

namespace Echoes
{
    /// <summary>A stateless Present-timeline rune tablet that forwards presses to the shared RunePuzzle.</summary>
    public class RuneControl : Interactable
    {
        public RunePuzzle puzzle;
        public KeyId glyph;
        public Renderer glyphRenderer;
        public Light glyphLight;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        MaterialPropertyBlock block;

        public override string GetPrompt(GameSession s) => "Press the " + EchoesIds.KeyTitle(glyph) + " rune";
        public override bool CanInteract(GameSession s) => puzzle != null && !puzzle.Solved;
        public override void Interact(GameSession s) => puzzle.Press(glyph);

        public void SetLit(bool solved, bool pressed)
        {
            if (block == null) block = new MaterialPropertyBlock();
            Color c = solved ? new Color(1.6f, 1.2f, 0.4f) : pressed ? new Color(0.4f, 1.6f, 2.6f) : new Color(0.1f, 0.4f, 0.7f);
            if (glyphRenderer != null)
            {
                block.SetColor(EmissionId, c);
                glyphRenderer.SetPropertyBlock(block);
            }
            if (glyphLight != null) glyphLight.intensity = solved ? 1.2f : pressed ? 1.5f : 0.35f;
        }
    }
}
