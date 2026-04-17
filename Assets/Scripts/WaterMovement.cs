public static class WaterMovement
{
    public const float SurfaceY = -0.245f;
    public const float InWaterSpeedMultiplier = 0.5f;

    public static float SpeedMultiplier(float worldY) => worldY < SurfaceY ? InWaterSpeedMultiplier : 1f;
}
