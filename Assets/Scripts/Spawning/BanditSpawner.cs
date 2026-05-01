using Medieval.Npcs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Spawns one DOTS bandit at this GameObject's position and rotation when play begins.
/// </summary>
public class BanditSpawner : MonoBehaviour
{
    [SerializeField] float uniformScale = 1f;

    void Start()
    {
        quaternion rot = new quaternion(
            transform.rotation.x,
            transform.rotation.y,
            transform.rotation.z,
            transform.rotation.w);

        var e = NpcSpawnApi.SpawnBandit(transform.position, rot, uniformScale);
        if (e == Unity.Entities.Entity.Null)
        {
            Debug.LogWarning(
                "BanditSpawner: NpcSpawnApi.SpawnBandit failed (is NpcPrefabRegistryAuthoring in a loaded subscene with Bandit prefab assigned?).");
        }
    }
}
