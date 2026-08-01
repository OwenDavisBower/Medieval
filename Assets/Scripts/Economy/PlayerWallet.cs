using System;
using UnityEngine;

/// <summary>
/// Player gold balance. Bootstraps at play start so scene wiring is not required.
/// </summary>
public sealed class PlayerWallet : MonoBehaviour
{
    public static PlayerWallet Instance { get; private set; }

    [SerializeField] int startingGold;

    int _gold;

    /// <summary>Current gold; never negative.</summary>
    public int Gold => _gold;

    /// <summary>Fired whenever <see cref="Gold"/> changes; argument is the new balance.</summary>
    public event Action<int> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<PlayerWallet>() != null)
            return;

        var go = new GameObject("PlayerWallet");
        go.AddComponent<PlayerWallet>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _gold = Mathf.Max(0, startingGold);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Add(int amount)
    {
        if (amount <= 0)
            return;
        _gold += amount;
        Changed?.Invoke(_gold);
    }

    public bool TrySpend(int amount)
    {
        if (amount <= 0)
            return true;
        if (_gold < amount)
            return false;
        _gold -= amount;
        Changed?.Invoke(_gold);
        return true;
    }

    public void SetGold(int amount)
    {
        int next = Mathf.Max(0, amount);
        if (next == _gold)
            return;
        _gold = next;
        Changed?.Invoke(_gold);
    }
}
