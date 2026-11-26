// Death timer and bad-connection indicator of one slot holder (contract 4.6).

namespace PortNet
{
    public sealed class Liveness
    {
        public float Death = NetLimits.DeathTime;
        public float Indicator = NetLimits.DeathTime;

        public bool IsBad => Indicator < NetLimits.BadConnectionThreshold;

        public void Reset()
        {
            Death = NetLimits.DeathTime;
        }

        public void SetNearZero(float lastDt)
        {
            Death = lastDt;
        }

        // One Update with heartbeats enabled. True when the timer crosses from above zero to
        // zero or below (the caller then stops the heartbeat).
        public bool Tick(float dt)
        {
            bool wasAlive = Death > 0f;
            if (Death >= 0f)
            {
                Death -= dt;
                if (Death < NetLimits.BadConnectionThreshold && Death < Indicator)
                    Indicator = Death;
                else
                    Indicator = 0.999f * Indicator + 0.001f * Death;
            }
            return wasAlive && Death <= 0f;
        }
    }
}
