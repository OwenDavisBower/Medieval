using UnityEngine;

/// <summary>
/// After procedural terrain is ready, runs spawners in order: settlements, bandit camps, trees.
/// </summary>
public class SpawningCoordinator : MonoBehaviour
{
    void OnEnable()
    {
        TerrainGenerator.TerrainGenerated += OnTerrainGenerated;
    }

    void OnDisable()
    {
        TerrainGenerator.TerrainGenerated -= OnTerrainGenerated;
    }

    void Start() => RunSpawnSequence();

    void OnTerrainGenerated(TerrainGenerator _) => RunSpawnSequence();

    void RunSpawnSequence()
    {
        var gen = Object.FindFirstObjectByType<TerrainGenerator>();
        if (gen == null || !gen.IsTerrainReady)
            return;

        foreach (var settlement in FindObjectsByType<SettlementSpawner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            settlement.TrySpawnSettlements();

        foreach (var bandit in FindObjectsByType<BanditCampSpawner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            bandit.SpawnCamps();

        foreach (var trees in FindObjectsByType<TreeSpawner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            trees.TrySpawnTrees();
    }
}
