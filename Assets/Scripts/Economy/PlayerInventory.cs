using System;
using UnityEngine;

/// <summary>Player cargo (wood / food). Gold lives in <see cref="PlayerWallet"/>.</summary>
public sealed class PlayerInventory : MonoBehaviour
{
    public static PlayerInventory Instance { get; private set; }

    [SerializeField] int startingWood;
    [SerializeField] int startingFood = 3;

    int _wood;
    int _food;

    public int Wood => _wood;
    public int Food => _food;

    public event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying)
            return;
        if (FindAnyObjectByType<PlayerInventory>() != null)
            return;

        var go = new GameObject("PlayerInventory");
        go.AddComponent<PlayerInventory>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _wood = Mathf.Max(0, startingWood);
        _food = Mathf.Max(0, startingFood);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void AddWood(int amount)
    {
        if (amount <= 0)
            return;
        _wood += amount;
        Changed?.Invoke();
    }

    public void AddFood(int amount)
    {
        if (amount <= 0)
            return;
        _food += amount;
        Changed?.Invoke();
    }

    public bool TrySpendWood(int amount)
    {
        if (amount <= 0)
            return true;
        if (_wood < amount)
            return false;
        _wood -= amount;
        Changed?.Invoke();
        return true;
    }

    public bool TrySpendFood(int amount)
    {
        if (amount <= 0)
            return true;
        if (_food < amount)
            return false;
        _food -= amount;
        Changed?.Invoke();
        return true;
    }

    public void SetWood(int amount)
    {
        int next = Mathf.Max(0, amount);
        if (next == _wood)
            return;
        _wood = next;
        Changed?.Invoke();
    }

    public void SetFood(int amount)
    {
        int next = Mathf.Max(0, amount);
        if (next == _food)
            return;
        _food = next;
        Changed?.Invoke();
    }

    /// <summary>Halves cargo (death penalty). Returns wood/food lost.</summary>
    public void ApplyDeathPenalty(out int woodLost, out int foodLost)
    {
        woodLost = _wood / 2;
        foodLost = _food / 2;
        _wood -= woodLost;
        _food -= foodLost;
        Changed?.Invoke();
    }
}
