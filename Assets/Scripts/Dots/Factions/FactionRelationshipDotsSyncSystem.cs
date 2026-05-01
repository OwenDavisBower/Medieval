using Unity.Entities;
using UnityEngine;

namespace Medieval.Dots.Factions
{
    /// <summary>Copies <see cref="FactionManager"/> into a singleton buffer for DOTS combat (main thread).</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(Medieval.Npcs.NpcCombatSeekSystemGroup))]
    public partial class FactionRelationshipDotsSyncSystem : SystemBase
    {
        EntityQuery _q;

        protected override void OnCreate()
        {
            _q = GetEntityQuery(ComponentType.ReadWrite<FactionRelationshipState>());
            if (_q.CalculateEntityCount() == 0)
            {
                Entity e = EntityManager.CreateEntity();
                EntityManager.AddComponentData(e, new FactionRelationshipState());
                EntityManager.AddBuffer<FactionRelationshipCell>(e);
#if UNITY_EDITOR
                EntityManager.SetName(e, "FactionRelationshipDotsSingleton");
#endif
            }
        }

        protected override void OnUpdate()
        {
            Entity singleton = _q.GetSingletonEntity();
            var buffer = EntityManager.GetBuffer<FactionRelationshipCell>(singleton);
            var state = EntityManager.GetComponentData<FactionRelationshipState>(singleton);

            FactionManager fm = FactionManager.Instance;
            if (fm == null)
            {
                if (state.MatrixSize != 0 || buffer.Length != 0)
                {
                    buffer.Clear();
                    state.MatrixSize = 0;
                    state.SourceVersion = 0;
                    EntityManager.SetComponentData(singleton, state);
                }

                return;
            }

            int size = fm.RelationshipMatrixSize;
            int version = fm.RelationshipMatrixVersion;
            int needed = size * size;
            if (version == state.SourceVersion && size == state.MatrixSize && buffer.Length == needed)
                return;

            buffer.Clear();
            if (needed > 0)
            {
                var tmp = new byte[needed];
                fm.CopyRelationshipMatrixBytes(tmp);
                buffer.ResizeUninitialized(needed);
                for (int i = 0; i < needed; i++)
                    buffer[i] = new FactionRelationshipCell { Value = tmp[i] };
            }

            state.MatrixSize = size;
            state.SourceVersion = version;
            EntityManager.SetComponentData(singleton, state);
        }
    }
}
