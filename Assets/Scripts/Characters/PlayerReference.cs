using UnityEngine;

/// <summary>Resolves the player transform for NPC anchoring and targeting.</summary>
public static class PlayerReference
{
    static Transform _player;

    /// <summary>Returns the player transform, or null if not found yet.</summary>
    public static Transform TryGetTransform()
    {
        if (_player == null)
        {
            var go = GameObject.Find("Player");
            if (go != null)
                _player = go.transform;
        }

        return _player;
    }
}
