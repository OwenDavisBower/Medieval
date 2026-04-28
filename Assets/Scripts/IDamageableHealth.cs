/// <summary>Combat targets that track health and can be destroyed at 0 (e.g. <see cref="Character"/>, <see cref="Building"/>).</summary>
public interface IDamageableHealth
{
    float CurrentHealth { get; }
    float MaxHealth { get; }
    bool IsDead { get; }
    void TakeDamage(float amount);
}
