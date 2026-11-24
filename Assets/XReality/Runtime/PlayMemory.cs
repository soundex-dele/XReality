using System;

namespace XReality
{
    // Each *completed* play counts once; dragging across a zone doesn't inflate its score.
    public sealed class PlayMemory
    {
        public const int Threshold = 3;
        public const int MaxScore = 12;
        public const float Boundary = 1.25f;
        public enum Zone { None, Rug, Window }
        public int Rug { get; private set; }
        public int Window { get; private set; }
        public Zone Favorite
        {
            get
            {
                if (Window >= Threshold && Window >= Rug + 2) return Zone.Window;
                if (Rug >= Threshold && Rug >= Window + 2) return Zone.Rug;
                return Zone.None;
            }
        }
        public PlayMemory(int rug, int window)
        {
            Rug = Clamp(rug);
            Window = Clamp(window);
        }
        public void Record(float x)
        {
            if (x >= Boundary) { Window = Clamp(Window + 1); Rug = Clamp(Rug - 1); }
            else { Rug = Clamp(Rug + 1); Window = Clamp(Window - 1); }
        }
        static int Clamp(int score) { return Math.Max(0, Math.Min(score, MaxScore)); }
    }
}
