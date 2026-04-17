using UnityEngine;

/// <summary>Places world XZ positions on the active terrain surface.</summary>
public static class TerrainSpawnUtility
{
    /// <summary>
    /// Sets <paramref name="worldPosition"/>.y to terrain height at that XZ plus <paramref name="heightOffset"/>.
    /// </summary>
    public static Vector3 GetWorldPositionOnTerrain(Vector3 worldPosition, float heightOffset = 0.05f)
    {
        Terrain terrain = Terrain.activeTerrain != null ? Terrain.activeTerrain : Object.FindFirstObjectByType<Terrain>();
        if (terrain != null)
        {
            worldPosition.y = terrain.SampleHeight(worldPosition) + heightOffset;
            return worldPosition;
        }

        var proc = TerrainGenerator.GetActiveOrFind();
        if (proc != null && proc.IsTerrainReady)
        {
            worldPosition.y = proc.SampleHeightWorldXZ(worldPosition.x, worldPosition.z) + heightOffset;
            return worldPosition;
        }

        worldPosition.y += heightOffset;
        return worldPosition;
    }
}
