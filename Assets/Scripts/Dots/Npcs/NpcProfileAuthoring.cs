using Medieval.NpcMovement;
using Unity.Entities;
using UnityEngine;

namespace Medieval.Npcs
{
    /// <summary>Optional: bake <see cref="NpcProfile"/> on NPC prefabs (e.g. subscene villagers). Spawn API overwrites <see cref="NpcProfile.Role"/>.</summary>
    [DisallowMultipleComponent]
    public sealed class NpcProfileAuthoring : MonoBehaviour
    {
        [Tooltip("Baked onto the entity; SpawnFollower/Bandit/Villager replace this with the spawn kind.")]
        public NpcRole InitialRole = NpcRole.Unknown;

        public NpcWeaponClass WeaponClass = NpcWeaponClass.Unspecified;

        class Baker : Baker<NpcProfileAuthoring>
        {
            public override void Bake(NpcProfileAuthoring authoring)
            {
                Entity entity = GetEntity(authoring, TransformUsageFlags.Dynamic);
                AddComponent(entity, new NpcProfile
                {
                    Role = authoring.InitialRole,
                    WeaponClass = authoring.WeaponClass
                });
                var aff = authoring.GetComponent<Affiliation>();
                int factionId = aff != null && aff.Faction != null ? aff.Faction.FactionID : -1;
                AddComponent(entity, new NpcFactionId { Value = factionId });

                var moveAuth = authoring.GetComponent<NpcMovementAuthoring>();
                if (moveAuth != null)
                {
                    DependsOn(moveAuth);
                    var cfg = NpcMovementAuthoring.BuildCombatSeekConfig(moveAuth);
                    cfg.SeeksCombatTargets = (byte)((authoring.InitialRole == NpcRole.Villager ||
                        authoring.InitialRole == NpcRole.Unknown)
                        ? 0
                        : 1);
                    AddComponent(entity, cfg);
                }
            }
        }
    }
}
