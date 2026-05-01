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
        static int s_PlayerFactionId = -1;

        public static void Register(Transform transform, Rigidbody rigidbody, int playerFactionId = -1)
        {
            s_Transform = transform;
            s_Rigidbody = rigidbody;
            s_PlayerFactionId = playerFactionId;
        }

        public static void Unregister(Transform transform)
        {
            if (s_Transform != transform)
                return;
            s_Transform = null;
            s_Rigidbody = null;
            s_PlayerFactionId = -1;
        }

        public static Transform Transform => s_Transform;
        public static Rigidbody Rigidbody => s_Rigidbody;
        public static bool HasPlayer => s_Transform != null;
        public static int PlayerFactionId => s_PlayerFactionId;
    }
}
