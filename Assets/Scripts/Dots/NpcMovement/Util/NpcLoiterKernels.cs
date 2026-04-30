using Unity.Mathematics;

namespace Medieval.NpcMovement
{
    /// <summary>Shared orbit / wander goal math for steering and pathfinding (Burst-safe).</summary>
    internal static class NpcLoiterKernels
    {
        public static float3 ComputeOrbit(in NpcMovementState mstate, in NpcMovementConfig cfg, in NpcAnchorTarget anchor,
            float elapsedTime)
        {
            float t = elapsedTime * cfg.NoiseFrequency;
            float angleJitter = (NpcMath.Perlin01(mstate.NoiseA, t) - 0.5f) * 2f *
                                math.radians(cfg.AngleWobbleDegrees);
            float r = mstate.BaseRadius +
                      (NpcMath.Perlin01(t, mstate.NoiseB) - 0.5f) * 2f * cfg.RadiusWobble;
            r = math.clamp(r, cfg.MinLoiterRadius, cfg.MaxLoiterRadius);
            float angle = mstate.BaseAngle + angleJitter;
            float3 offset = new float3(math.sin(angle), 0f, math.cos(angle)) * r;

            float3 trail = float3.zero;
            if (cfg.TrailBehindStrength > 0f)
            {
                float3 pv = anchor.LinearVelocity;
                pv.y = 0f;
                float mag = math.length(pv);
                if (mag > 0.05f)
                    trail = -math.normalize(pv) * math.min(mag * cfg.TrailBehindStrength, cfg.MaxTrailOffset);
            }

            float3 flat = offset + trail;
            flat.y = 0f;
            float minR = cfg.MinLoiterRadius;
            float len = math.length(flat);
            if (len < minR)
            {
                if (len > 1e-6f)
                    flat = math.normalize(flat) * minR;
                else
                    flat = new float3(math.sin(angle), 0f, math.cos(angle)) * minR;
            }

            return anchor.Position + flat;
        }

        /// <summary>Call once per entity per frame from steering before <see cref="ComputeWanderPosition"/>.</summary>
        public static void AdvanceWanderRepick(ref NpcMovementState mstate, in NpcMovementConfig cfg, in NpcAnchorTarget anchor,
            float elapsedTime)
        {
            if (elapsedTime >= mstate.NextWanderPickTime)
            {
                float jitter = 0.7f + mstate.Rng.NextFloat() * 0.6f;
                mstate.NextWanderPickTime = elapsedTime + cfg.RepickWanderInterval * jitter;

                float2 disk = mstate.Rng.NextFloat2Direction() * mstate.Rng.NextFloat() * cfg.WanderRadius;
                mstate.BaseRadius = math.length(disk);
                mstate.BaseAngle = mstate.BaseRadius > 1e-5f ? math.atan2(disk.x, disk.y) : 0f;
            }
        }

        /// <summary>Wander sample from current state without advancing repick (for pathfinding).</summary>
        public static float3 ComputeWanderPosition(in NpcMovementState mstate, in NpcMovementConfig cfg, in NpcAnchorTarget anchor,
            float elapsedTime)
        {
            float t = elapsedTime * cfg.NoiseFrequency;
            float angleJitter = (NpcMath.Perlin01(mstate.NoiseA, t) - 0.5f) * 2f *
                                math.radians(cfg.AngleWobbleDegrees);
            float rWobble = (NpcMath.Perlin01(t, mstate.NoiseB) - 0.5f) * 2f * cfg.RadiusWobble;
            float angle = mstate.BaseAngle + angleJitter;
            float r = math.clamp(mstate.BaseRadius + rWobble, 0f, cfg.WanderRadius);
            float3 offset = new float3(math.sin(angle), 0f, math.cos(angle)) * r;
            return anchor.Position + offset;
        }
    }
}
