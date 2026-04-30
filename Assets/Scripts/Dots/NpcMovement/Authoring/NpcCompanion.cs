using Unity.Entities;
using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Managed per-entity link to the NPC GameObject: holds the companion <see cref="UnityEngine.Transform"/>
    /// and <see cref="UnityEngine.Rigidbody"/> the DOTS integration system drives, plus a facade callback the
    /// writeback system uses to push velocity/effective-speed back to <c>LocomotionAnimatorDriver</c>.
    /// Sync systems run on the main thread because this component is managed.
    /// </summary>
    public class NpcCompanion : IComponentData
    {
        public Transform Transform;
        public Rigidbody Rigidbody;
        public INpcFacade Facade;
    }
}
