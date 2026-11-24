using UnityEngine;

namespace XReality
{
    // Separates sample collection from camera I/O so the same rules can be tested in the built player.
    internal sealed class NeutralCalibration
    {
        const double RequiredSeconds = .85;
        int count;
        double started, previous;
        FacePose average;
        public bool Calibrated { get; private set; }
        public bool Pending { get; private set; }
        public float Progress => count == 0 ? 0 : (float)((previous - started) / RequiredSeconds);

        public void Begin() { Pending = true; count = 0; }
        public void Interrupt() { count = 0; }
        public void Reset() { Pending = Calibrated = false; count = 0; average = FacePose.Neutral; }
        public void Add(FacePose raw, double now)
        {
            if (!Pending) return;
            if (raw.jaw > .28f || raw.left > .55f || raw.right > .55f) { Interrupt(); return; }
            if (count > 0 && (now - previous > .25 || Quaternion.Angle(raw.rotation, average.rotation) > 8)) Interrupt();
            if (count == 0) { started = now; average = raw; }
            else
            {
                float w = 1f / (count + 1);
                average.rotation = Quaternion.Slerp(average.rotation, raw.rotation, w);
                average.left = Mathf.Lerp(average.left, raw.left, w);
                average.right = Mathf.Lerp(average.right, raw.right, w);
                average.jaw = Mathf.Lerp(average.jaw, raw.jaw, w);
            }
            count++;
            previous = now;
            if (now - started < RequiredSeconds || count < 6) return;
            Calibrated = true;
            Pending = false;
        }
        public FacePose Apply(FacePose raw)
        {
            if (Pending) return FacePose.Neutral;
            if (!Calibrated) return raw;
            return new FacePose
            {
                rotation = Quaternion.Inverse(average.rotation) * raw.rotation,
                left = Mathf.Clamp01((raw.left - average.left - .028f) / Mathf.Max(.30f, 1 - average.left)),
                right = Mathf.Clamp01((raw.right - average.right - .028f) / Mathf.Max(.30f, 1 - average.right)),
                jaw = Mathf.Clamp01((raw.jaw - average.jaw - .022f) / Mathf.Max(.38f, .75f - average.jaw))
            };
        }
    }
}
