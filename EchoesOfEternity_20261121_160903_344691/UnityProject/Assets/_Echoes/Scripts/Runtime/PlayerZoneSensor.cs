using System.Collections.Generic;
using UnityEngine;

namespace Echoes
{
    /// <summary>
    /// Reads gameplay trigger volumes (layer EE_Zones) with an explicit capsule overlap each frame.
    /// This avoids depending on trigger callbacks and keeps zone state deterministic.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class PlayerZoneSensor : MonoBehaviour
    {
        public FirstPersonMotor motor;

        public readonly List<TimedSpikeTrap> Traps = new List<TimedSpikeTrap>();
        public readonly List<HintZone> Hints = new List<HintZone>();
        public bool InSafeZone { get; private set; }
        public bool InNoSwitchZone { get; private set; }
        public Checkpoint Checkpoint { get; private set; }
        public ExitTrigger Exit { get; private set; }

        void Update() => Refresh();

        public void Refresh()
        {
            Traps.Clear();
            Hints.Clear();
            InSafeZone = false;
            InNoSwitchZone = false;
            Checkpoint = null;
            Exit = null;
            if (motor == null) return;

            motor.GetCapsule(motor.transform.position, 0f, out Vector3 top, out Vector3 bottom, out float radius);
            Collider[] hits = Physics.OverlapCapsule(top, bottom, radius, EchoesLayers.ZoneMask, QueryTriggerInteraction.Collide);
            foreach (var h in hits)
            {
                if (h.GetComponent<TimelineSafeZone>() != null) InSafeZone = true;
                if (h.GetComponent<NoSwitchZone>() != null) InNoSwitchZone = true;
                var trap = h.GetComponentInParent<TimedSpikeTrap>();
                if (trap != null)
                {
                    InNoSwitchZone = true;
                    if (!Traps.Contains(trap)) Traps.Add(trap);
                }
                var cp = h.GetComponentInParent<Checkpoint>();
                if (cp != null) Checkpoint = cp;
                var exit = h.GetComponent<ExitTrigger>();
                if (exit != null) Exit = exit;
                var hint = h.GetComponent<HintZone>();
                if (hint != null) Hints.Add(hint);
            }
        }
    }
}
