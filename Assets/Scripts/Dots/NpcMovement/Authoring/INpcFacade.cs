using Unity.Mathematics;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Contract between the DOTS NPC movement pipeline and its owning MonoBehaviour (the
    /// <c>TargetSteeringMotor</c> facade). Input configuration is pushed by the facade directly via
    /// <c>EntityManager</c>; this interface only carries per-frame output back to the GameObject side
    /// (animation driver, dodge cooldown tracking).
    /// </summary>
    public interface INpcFacade
    {
        /// <summary>
        /// Called on the main thread after each NPC movement tick with the latest simulation state.
        /// </summary>
        void OnMovementStateSynced(float3 horizontalVelocity, float effectiveMoveSpeed, bool hasPendingDodge);
    }
}
