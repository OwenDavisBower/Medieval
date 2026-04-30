using ProjectDawn.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace ProjectDawn.Sample
{
    [MaterialProperty("_TeamShift")]
    public struct TeamShift : IComponentData
    {
        public float Value;
    }

    public class TeamSwitcher : MonoBehaviour
    {
        void Start()
        {
            var renderMeshArray = GetComponent<RenderMeshArrayAuthoring>();
            var entity = renderMeshArray.GetOrCreateEntity();
            World.DefaultGameObjectInjectionWorld.EntityManager.AddComponent<TeamShift>(entity);
        }

        void Update()
        {
            var renderMeshArray = GetComponent<RenderMeshArrayAuthoring>();
            var entity = renderMeshArray.GetOrCreateEntity();
            World.DefaultGameObjectInjectionWorld.EntityManager.SetComponentData(entity, new TeamShift
            {
                Value = math.abs(math.sin(Time.timeSinceLevelLoad)),
            });
        }
    }
}