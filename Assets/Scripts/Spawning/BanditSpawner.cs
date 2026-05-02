using Medieval.Npcs;
using Medieval.NpcMovement;
using Unity.Entities;
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

        // Yaw only so a tilted spawner transform does not rotate the NPC into a bad pose; matches BanditCamp-style upright spawns.
        quaternion rot = quaternion.RotateY(math.radians(transform.eulerAngles.y));
        float3 anchorPos = new float3(transform.position.x, transform.position.y, transform.position.z);

        World world = World.DefaultGameObjectInjectionWorld;
        bool canSetAnchor = world != null && world.IsCreated;

        for (int i = 0; i < spawnCount; i++)
        {
            var wc = NpcSpawnApi.WeaponClassForHalfMeleeHalfRangedSplit(i, spawnCount);
            var e = NpcSpawnApi.SpawnBandit(transform.position, rot, uniformScale, wc);
            if (e == Entity.Null)
            {
                Debug.LogWarning(
                    "BanditSpawner: NpcSpawnApi.SpawnBandit failed (is NpcPrefabRegistryAuthoring in a loaded subscene with Bandit prefab assigned?).");
                break;
            }

            // Same as BanditCamp: orbit/wander steering needs NpcAnchorTarget.HasAnchor or there is no goal until combat seek overrides.
            if (canSetAnchor)
                NpcMovementApi.SetAnchorPosition(world.EntityManager, e, anchorPos);
        }
    }
}
