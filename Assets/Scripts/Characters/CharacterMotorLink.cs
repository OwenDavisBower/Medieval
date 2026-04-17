using UnityEngine;

/// <summary>Shared hookup between <see cref="Character"/> stats and <see cref="TargetSteeringMotor"/>.</summary>
public static class CharacterMotorLink
{
    public static void ApplyMovementSpeed(Character character, TargetSteeringMotor motor)
    {
        if (character != null)
            motor.MoveSpeedScale = character.MovementSpeedMultiplier;
    }
}
