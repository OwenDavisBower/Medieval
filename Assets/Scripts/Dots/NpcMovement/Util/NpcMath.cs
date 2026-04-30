using Unity.Mathematics;

namespace Medieval.NpcMovement
{
    /// <summary>Burst-friendly math helpers shared by NPC movement jobs.</summary>
    internal static class NpcMath
    {
        public const float WaterSurfaceY = -0.249f;
        public const float InWaterSpeedMultiplier = 0.5f;

        public static float WaterSpeedMultiplier(float worldY) =>
            worldY < WaterSurfaceY ? InWaterSpeedMultiplier : 1f;

        /// <summary>
        /// Burst-friendly reimplementation of <c>UnityEngine.Vector3.SmoothDamp</c> without the max-speed
        /// clamp. Deterministic and allocation-free; mirrors Unity's reference implementation.
        /// </summary>
        public static float3 SmoothDamp(
            float3 current,
            float3 target,
            ref float3 currentVelocity,
            float smoothTime,
            float deltaTime)
        {
            smoothTime = math.max(0.0001f, smoothTime);
            float omega = 2f / smoothTime;
            float x = omega * deltaTime;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float3 change = current - target;
            float3 originalTarget = target;
            float3 temp = (currentVelocity + omega * change) * deltaTime;
            currentVelocity = (currentVelocity - omega * temp) * exp;
            float3 output = (current - change) + (change + temp) * exp;
            if (math.dot(originalTarget - current, output - originalTarget) > 0f)
            {
                output = originalTarget;
                currentVelocity = float3.zero;
            }
            return output;
        }

        /// <summary>Uses Unity.Mathematics noise as a drop-in for <c>Mathf.PerlinNoise</c>, remapped to [0,1].</summary>
        public static float Perlin01(float x, float y)
        {
            return math.saturate(noise.cnoise(new float2(x, y)) * 0.5f + 0.5f);
        }

        public static float3 MoveTowards(float3 current, float3 target, float maxDelta)
        {
            float3 diff = target - current;
            float sq = math.lengthsq(diff);
            if (sq <= maxDelta * maxDelta || sq < 1e-8f)
                return target;
            return current + diff / math.sqrt(sq) * maxDelta;
        }

        /// <summary>True if some point on segment PQ (XZ) lies strictly inside a horizontal disk around C.</summary>
        public static bool HorizSegmentEntersDisk(float2 p, float2 q, float2 c, float radius)
        {
            if (radius <= 0f)
                return false;
            float2 v = q - p;
            float vv = math.dot(v, v);
            if (vv < 1e-12f)
                return math.distance(p, c) < radius;
            float t = math.saturate(math.dot(c - p, v) / vv);
            float2 closest = p + v * t;
            return math.distance(closest, c) < radius;
        }

        /// <summary>
        /// When the straight path from self to seek would cut through the anchor's min-loiter disc, steer toward
        /// a point on the disc rim along the shorter horizontal arc toward <paramref name="rawGoal"/>.
        /// </summary>
        public static float3 AdjustSeekAroundAnchorDisc(float3 selfPos, float3 seekPoint, float3 anchorPos, float minRadius,
            float3 rawGoal)
        {
            if (minRadius <= 1e-4f)
                return seekPoint;

            float2 p = selfPos.xz;
            float2 q = seekPoint.xz;
            float2 c = anchorPos.xz;
            if (!HorizSegmentEntersDisk(p, q, c, minRadius))
                return seekPoint;

            float3 a = anchorPos;
            a.y = selfPos.y;
            float angSelf = math.atan2(selfPos.x - a.x, selfPos.z - a.z);
            float angGoal = math.atan2(rawGoal.x - a.x, rawGoal.z - a.z);
            float diff = angGoal - angSelf;
            const float twoPi = 2f * math.PI;
            diff -= twoPi * math.round(diff / twoPi);
            float step = math.sign(diff) * math.min(math.abs(diff), 0.55f);
            float ang = angSelf + step;
            float3 rim = a + new float3(math.sin(ang), 0f, math.cos(ang)) * minRadius;
            rim.y = seekPoint.y;
            return rim;
        }
    }
}

