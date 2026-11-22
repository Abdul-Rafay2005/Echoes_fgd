using System.Collections.Generic;
using UnityEngine;

namespace Echoes
{
    /// <summary>Owns the list of checkpoints and their stable IDs.</summary>
    public class CheckpointManager : MonoBehaviour
    {
        public string defaultId = "hub";
        public Checkpoint[] checkpoints;

        public List<string> Ids
        {
            get
            {
                var ids = new List<string>();
                if (checkpoints != null)
                    foreach (var c in checkpoints) if (c != null) ids.Add(c.id);
                return ids;
            }
        }

        public Checkpoint Get(string id)
        {
            if (checkpoints == null) return null;
            foreach (var c in checkpoints)
                if (c != null && c.id == id) return c;
            return null;
        }

        public Checkpoint Default => Get(defaultId);

        public void MarkCurrent(string id)
        {
            if (checkpoints == null) return;
            foreach (var c in checkpoints)
                if (c != null) c.SetCurrent(c.id == id);
        }
    }
}
