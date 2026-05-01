using UnityEngine;

namespace Medieval.NpcMovement
{
    /// <summary>
    /// Transform/rigidbody for the active player, set once from <c>PlayerController</c> (no tag/name Find).
    /// DOTS systems in this assembly read this; gameplay code registers via <see cref="PlayerReference"/>.
    /// </summary>
    public static class PlayerAnchorRegistration
    {
        static Transform s_Transform;
        static Rigidbody s_Rigidbody;

        public static void Register(Transform transform, Rigidbody rigidbody)
        {
            s_Transform = transform;
            s_Rigidbody = rigidbody;
        }

        public static void Unregister(Transform transform)
        {
            if (s_Transform != transform)
                return;
            s_Transform = null;
            s_Rigidbody = null;
        }

        public static Transform Transform => s_Transform;
        public static Rigidbody Rigidbody => s_Rigidbody;
        public static bool HasPlayer => s_Transform != null;
    }
}
