namespace XReality
{
    public enum Mood
    {
        Watch,
        Sleep,
        Nuzzle,
        EarFlick,
        Notice,
        Chase
    }

    // Pure clock-driven state machine. Unity owns movement; the brain owns transition timing.
    public sealed class PetBrain
    {
        public Mood State { get; private set; }

        public PetBrain()
        {
            State = Mood.Watch;
        }

        double until = 12;
        public bool Stroke(double now)
        {
            if (State != Mood.Watch && State != Mood.Sleep)
                return false;
            bool sleepy = State == Mood.Sleep;
            Enter(sleepy ? Mood.EarFlick : Mood.Nuzzle, now + (sleepy ? .65 : 1.05));
            return true;
        }

        public void OfferToy(double now)
        {
            if (State == Mood.Notice || State == Mood.Chase)
                return;
            Enter(Mood.Notice, now + .55);
        }

        public void Tick(double now, bool atToy)
        {
            if (now < until)
                return;
            switch (State)
            {
                case Mood.Nuzzle:
                case Mood.Sleep:
                    Enter(Mood.Watch, now + 12);
                    break;
                case Mood.EarFlick:
                case Mood.Watch:
                    Enter(Mood.Sleep, now + 8);
                    break;
                case Mood.Notice:
                    Enter(Mood.Chase, now);
                    break;
                case Mood.Chase:
                    if (atToy)
                        Enter(Mood.Watch, now + 12);
                    break;
            }
        }

        void Enter(Mood state, double deadline)
        {
            State = state;
            until = deadline;
        }
    }
}
