using Medieval.Npcs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Spawns DOTS bandits at this GameObject's position and rotation when play begins.
/// </summary>
public class BanditSpawner : MonoBehaviour
{
    [SerializeField] [Min(0)] [Tooltip("Inclusive lower bound; spawn count is chosen at random each play.")]
    int minCount = 1;
    [SerializeField] [Min(0)] [Tooltip("Inclusive upper bound; must be >= Min Count (values are swapped if not).")]
    int maxCount = 1;
    [SerializeField] float uniformScale = 1f;

    void Start()
    {
        int min = math.min(minCount, maxCount);
        int max = math.max(minCount, maxCount);
        min = math.max(0, min);
        max = math.max(0, max);
        int spawnCount = min == max ? min : UnityEngine.Random.Range(min, max + 1);

        quaternion rot = new quaternion(
            transform.rotation.x,
            transform.rotation.y,
            transform.rotation.z,
            transform.rotation.w);

        for (int i = 0; i < spawnCount; i++)
        {
            var e = NpcSpawnApi.SpawnBandit(transform.position, rot, uniformScale);
            if (e == Unity.Entities.Entity.Null)
            {
                Debug.LogWarning(
                    "BanditSpawner: NpcSpawnApi.SpawnBandit failed (is NpcPrefabRegistryAuthoring in a loaded subscene with Bandit prefab assigned?).");
                break;
            }
        }
    }
}
