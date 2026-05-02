using Medieval.Npcs;
using Medieval.NpcMovement;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Spawns a DOTS villager with the chop-wood task at play start. Assign <see cref="dropOffPoint"/> to the stockpile or building entrance.
/// </summary>
public class LumberjackVillagerSpawner : MonoBehaviour
{
    [Tooltip("World location where gathered wood is delivered. If unset, uses this object's position.")]
    [SerializeField] Transform dropOffPoint;

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

        quaternion rot = quaternion.RotateY(math.radians(transform.eulerAngles.y));
        Vector3 dropWorld = dropOffPoint != null ? dropOffPoint.position : transform.position;
        float3 anchorPos = new float3(transform.position.x, transform.position.y, transform.position.z);

        World world = World.DefaultGameObjectInjectionWorld;
        bool canSetAnchor = world != null && world.IsCreated;

        for (int i = 0; i < spawnCount; i++)
        {
            var e = NpcSpawnApi.SpawnWoodChopperVillager(transform.position, rot, uniformScale, dropWorld);
            if (e == Entity.Null)
            {
                Debug.LogWarning(
                    "LumberjackVillagerSpawner: NpcSpawnApi.SpawnWoodChopperVillager failed (is NpcPrefabRegistryAuthoring in a loaded subscene with Villager prefab assigned?).");
                break;
            }

            if (canSetAnchor)
                NpcMovementApi.SetAnchorPosition(world.EntityManager, e, anchorPos);
        }
    }
}
