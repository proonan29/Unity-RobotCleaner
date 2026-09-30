using UnityEngine;

namespace CleanerBot
{
    [System.Serializable]
    public struct BatterySpec
    {
        public bool enabled;
        [Tooltip("Minutes of continuous driving on a full battery")] public float capacityMinutes;
        [Tooltip("Minutes to charge from 0 to 100 %")] public float chargeMinutes;
        [Tooltip("Battery that must remain after reaching the dock (%)")] public float reservePercent;
        [Tooltip("Multiplier on the estimated energy needed to get home")] public float safetyFactor;
        [Tooltip("Resume cleaning when charged to this level (%)")] public float resumeAtPercent;
        [Tooltip("Drain while turning on the spot, relative to driving")] public float turnDrainFactor;
        [Tooltip("Drain while standing still, relative to driving")] public float idleDrainFactor;

        public static BatterySpec Default => new BatterySpec
        {
            enabled = true,
            capacityMinutes = 15f,
            chargeMinutes = 30f,
            reservePercent = 5f,
            safetyFactor = 1.25f,
            resumeAtPercent = 100f,
            turnDrainFactor = 0.8f,
            idleDrainFactor = 0.1f,
        };
    }

    /// <summary>Simple linear battery: drains with driving/turning/idle time, charges linearly at the dock.</summary>
    public class BatterySim
    {
        public BatterySpec Spec;
        public float Percent = 100f;

        public BatterySim(BatterySpec spec) { Spec = spec; }

        public float DrainPerSecond => 100f / (Mathf.Max(0.1f, Spec.capacityMinutes) * 60f);
        public float ChargePerSecond => 100f / (Mathf.Max(0.1f, Spec.chargeMinutes) * 60f);

        public void Drain(float driveSeconds, float turnSeconds, float idleSeconds)
        {
            if (!Spec.enabled) return;
            Percent -= DrainPerSecond * (driveSeconds + turnSeconds * Spec.turnDrainFactor + idleSeconds * Spec.idleDrainFactor);
            if (Percent < 0f) Percent = 0f;
        }

        public void Charge(float seconds) => Percent = Mathf.Min(100f, Percent + ChargePerSecond * seconds);

        /// <summary>Battery % needed to drive `meters` (with safety factor) and still keep the reserve.</summary>
        public float PercentNeededFor(float meters, float speed) =>
            meters / Mathf.Max(0.01f, speed) * DrainPerSecond * Spec.safetyFactor + Spec.reservePercent;
    }
}
