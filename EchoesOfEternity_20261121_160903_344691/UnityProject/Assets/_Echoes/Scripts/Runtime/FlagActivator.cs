using UnityEngine;

namespace Echoes
{
    /// <summary>Enables/disables objects (lights, markers) from a permanent progress flag.</summary>
    public class FlagActivator : ProgressListener
    {
        public string flag;
        public GameObject[] enableWhenSet;
        public GameObject[] disableWhenSet;

        protected override void Refresh(RunProgress progress, bool instant)
        {
            bool set = progress.HasFlag(flag);
            if (enableWhenSet != null) foreach (var g in enableWhenSet) if (g != null) g.SetActive(set);
            if (disableWhenSet != null) foreach (var g in disableWhenSet) if (g != null) g.SetActive(!set);
        }
    }
}
