using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Seed for one instanced mesh (layout matches <c>MeshInstance.compute</c> when used on GPU).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RockInstanceSeed
{
    public Vector4 PositionAndYaw;
    /// <summary>X: uniform scale. Y: mesh variant index into <see cref="MeshSpawnConfig.MeshVariants"/>.</summary>
    public Vector4 ScaleAndPad;

    public static int Stride => Marshal.SizeOf(typeof(RockInstanceSeed));
}
