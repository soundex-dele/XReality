using UnityEngine;

namespace XReality
{
    // Separates sample collection from camera I/O so the same rules can be tested in the built player.
    internal sealed class NeutralCalibration
    {
        const double RequiredSeconds = .85, MaxSampleGap = .25;
        const float MaxAngle = 8;
        int count;
        double acceptedSeconds, previous;
        bool skippedBlink;
        Quaternion anchor;
        FacePose average;
        public bool Calibrated { get; private set; }
        public bool Pending { get; private set; }
        public float Progress => Mathf.Clamp01((float)(acceptedSeconds / RequiredSeconds));

        public void Begin() { Pending = true; Interrupt(); }
        public void Interrupt() { count = 0; acceptedSeconds = 0; skippedBlink = false; }
        public void Reset() { Pending = Calibrated = false; Interrupt(); average = FacePose.Neutral; }
        public void Add(FacePose raw, double now)
        {
            if (!Pending) return;
            if (raw.jaw > .28f) { Interrupt(); return; }
            double interval = now - previous;
            if (count > 0 && (interval <= 0 || interval > MaxSampleGap || Quaternion.Angle(raw.rotation, anchor) > MaxAngle))
                Interrupt();
            // A normal blink pauses the timer without biasing the neutral eye weights.
            if (raw.left > .55f || raw.right > .55f) { skippedBlink = true; return; }
            if (count == 0) { anchor = raw.rotation; average = raw; }
            else
            {
                if (!skippedBlink) acceptedSeconds += interval;
                float w = 1f / (count + 1);
                average.rotation = Quaternion.Slerp(average.rotation, raw.rotation, w);
                average.left = Mathf.Lerp(average.left, raw.left, w);
                average.right = Mathf.Lerp(average.right, raw.right, w);
                average.jaw = Mathf.Lerp(average.jaw, raw.jaw, w);
                average.lipRound = Mathf.Lerp(average.lipRound, raw.lipRound, w);
                average.lipPress = Mathf.Lerp(average.lipPress, raw.lipPress, w);
                average.gazeLeft = Vector2.Lerp(average.gazeLeft, raw.gazeLeft, w);
                average.gazeRight = Vector2.Lerp(average.gazeRight, raw.gazeRight, w);
                average.smileLeft = Mathf.Lerp(average.smileLeft, raw.smileLeft, w);
                average.smileRight = Mathf.Lerp(average.smileRight, raw.smileRight, w);
                average.browLeft = Mathf.Lerp(average.browLeft, raw.browLeft, w);
                average.browRight = Mathf.Lerp(average.browRight, raw.browRight, w);
            }
            count++;
            previous = now;
            skippedBlink = false;
            if (acceptedSeconds < RequiredSeconds || count < 6) return;
            Calibrated = true;
            Pending = false;
        }
        // Absorb resting asymmetry and low-confidence wiggle without boosting a tiny range.
        static float Expressive(float raw, float rest) => Mathf.Clamp01((raw - rest - .04f) / Mathf.Max(.45f, 1 - rest));

        public FacePose Apply(FacePose raw)
        {
            if (Pending) return FacePose.Neutral;
            if (!Calibrated) return raw;
            return new FacePose
            {
                rotation = Quaternion.Inverse(average.rotation) * raw.rotation,
                left = Mathf.Clamp01((raw.left - average.left - .028f) / Mathf.Max(.30f, 1 - average.left)),
                right = Mathf.Clamp01((raw.right - average.right - .028f) / Mathf.Max(.30f, 1 - average.right)),
                jaw = Mathf.Clamp01((raw.jaw - average.jaw - .022f) / Mathf.Max(.38f, .75f - average.jaw)),
                lipRound = Expressive(raw.lipRound, average.lipRound),
                lipPress = Expressive(raw.lipPress, average.lipPress),
                gazeLeft = raw.gazeLeft - average.gazeLeft,
                gazeRight = raw.gazeRight - average.gazeRight,
                smileLeft = Expressive(raw.smileLeft, average.smileLeft),
                smileRight = Expressive(raw.smileRight, average.smileRight),
                browLeft = Expressive(raw.browLeft, average.browLeft),
                browRight = Expressive(raw.browRight, average.browRight)
            };
        }
    }
}
