using Medieval.DotsCombat;
using Unity.Entities;
using UnityEngine;

namespace Medieval.DotsCombatAuthoring
{
    [DisallowMultipleComponent]
    public sealed class FactionAuthoring : MonoBehaviour
    {
        [Tooltip("Faction ID matching FactionDefinition.FactionID.")]
        public int factionId = -1;

        class Baker : Baker<FactionAuthoring>
        {
            public override void Bake(FactionAuthoring authoring)
            {
                Entity e = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(e, new Faction { Id = authoring.factionId });
            }
        }
    }
}

