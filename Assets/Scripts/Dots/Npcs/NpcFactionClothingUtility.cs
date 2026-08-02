using ProjectDawn.Animation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Medieval.Npcs
{
    /// <summary>Per-entity clothing color for Entities Graphics (<c>_BaseColor</c> on tintable NPC materials).</summary>
    [MaterialProperty("_BaseColor")]
    public struct NpcClothingBaseColor : IComponentData
    {
        public float4 Value;
    }

    /// <summary>Applies <see cref="FactionDefinition.ClothingColor"/> to rendered mesh entities under an NPC root.</summary>
    public static class NpcFactionClothingUtility
    {
        public static void ApplyClothingColorForFaction(EntityManager em, Entity npcRoot, int factionId)
        {
            if (!em.Exists(npcRoot))
                return;

            Color color = Color.white;
            if (FactionManager.Instance != null)
                FactionManager.Instance.TryGetClothingColor(factionId, out color);
            else
                color = FallbackClothingColor(factionId);

            ApplyClothingColor(em, npcRoot, color);
        }

        public static void ApplyClothingColor(EntityManager em, Entity npcRoot, Color color)
        {
            if (!em.Exists(npcRoot))
                return;

            float4 value = new float4(color.r, color.g, color.b, color.a);

            // Collect targets first — AddComponentData is a structural change that invalidates LinkedEntityGroup.
            // Only skinned body meshes: weapon RenderMeshArray entities share the group but multiply full albedo by _BaseColor.
            var targets = new NativeList<Entity>(8, Allocator.Temp);
            if (IsTintableBodyMesh(em, npcRoot))
                targets.Add(npcRoot);

            if (em.HasBuffer<LinkedEntityGroup>(npcRoot))
            {
                var group = em.GetBuffer<LinkedEntityGroup>(npcRoot);
                for (int i = 0; i < group.Length; i++)
                {
                    Entity e = group[i].Value;
                    if (!em.Exists(e) || e == npcRoot)
                        continue;
                    if (!IsTintableBodyMesh(em, e))
                        continue;
                    targets.Add(e);
                }
            }

            for (int i = 0; i < targets.Length; i++)
                SetClothingColor(em, targets[i], value);
            targets.Dispose();
        }

        static bool IsTintableBodyMesh(EntityManager em, Entity e) =>
            em.HasComponent<MaterialMeshInfo>(e) && em.HasComponent<SkinMatrixBufferIndex>(e);

        static void SetClothingColor(EntityManager em, Entity e, float4 value)
        {
            if (em.HasComponent<NpcClothingBaseColor>(e))
                em.SetComponentData(e, new NpcClothingBaseColor { Value = value });
            else
                em.AddComponentData(e, new NpcClothingBaseColor { Value = value });
        }

        static Color FallbackClothingColor(int factionId) => factionId switch
        {
            0 => new Color(0.03f, 0.06f, 0.22f, 1f),
            1 => new Color(0.22f, 0.025f, 0.02f, 1f),
            2 => new Color(0.82f, 0.74f, 0.58f, 1f),
            _ => Color.white
        };
    }
}
