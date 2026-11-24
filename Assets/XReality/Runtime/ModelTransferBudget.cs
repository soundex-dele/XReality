namespace XReality
{
    // The wall-clock cap also bounds a server that sends tiny amounts indefinitely.
    internal struct ModelTransferBudget
    {
        internal const int AttemptSeconds = 90;
        internal const int StallSeconds = 24;
        readonly double started;
        double lastProgress;
        ulong received;

        internal ModelTransferBudget(double now)
        {
            started = lastProgress = now;
            received = 0;
        }

        internal bool Expired(double now, ulong downloaded)
        {
            if (downloaded > received)
            {
                received = downloaded;
                lastProgress = now;
            }
            return now - started >= AttemptSeconds || now - lastProgress >= StallSeconds;
        }
    }
}
