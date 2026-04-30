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
    }
}
