using UnityEngine;

namespace Echoes
{
    /// <summary>Named world anchors used by the automated test harness. Created by the project builder.</summary>
    public class EchoesLevelInfo : MonoBehaviour
    {
        public Transform startPoint;
        public Transform hubSafe;
        public Transform wallTest;
        public Transform presentBarrierCell;
        public Transform pastBarrierCell;
        public Transform cacheFront;
        public Transform trapPlate;
        public Transform spikeStart;
        public Transform leafShortcutInside;
        public Transform landingA;
        public Transform landingB;
        public Transform pitDrop;
        public Transform heartDoorFront;
        public Transform heartFront;
        public Transform exitInside;
        public Transform exitApproach;
        public KeyPickup sunKey;
        public KeyPickup leafKey;
        public ChestInteractable chest;
        public KeyDoor heartDoor;
        public HeartPickup heart;
        public FlagGate runeCache;
        public FlagGate leafShortcut;
        public FlagGate waveShortcut;
        public FlagGate heartGate;
        public FlagGate exitGate;
        public GameObject exitGuide;
    }
}
