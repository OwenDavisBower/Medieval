using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Toggles player debug stats on the G key (1000 HP, fast move, invulnerable).</summary>
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(Character))]
public class PlayerGodMode : MonoBehaviour
{
    [SerializeField] float godModeMaxHealth = 1000f;
    [SerializeField] float godModeMoveSpeed = 25f;

    PlayerController _player;
    Character _character;
    bool _active;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        _character = GetComponent<Character>();
    }

    void Update()
    {
        var k = Keyboard.current;
        if (k == null || !k.gKey.wasPressedThisFrame)
            return;

        _active = !_active;
        if (_active)
        {
            _character.SetGodMode(true, godModeMaxHealth);
            _player.SetGodMode(true, godModeMoveSpeed);
        }
        else
        {
            _character.SetGodMode(false, godModeMaxHealth);
            _player.SetGodMode(false, godModeMoveSpeed);
        }
    }
}
