using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Seed for one rock instance (layout still matches <c>RocksInstance.compute</c> if used elsewhere).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RockInstanceSeed
{
    public Vector4 PositionAndYaw;
    public Vector4 ScaleAndPad;

    public static int Stride => Marshal.SizeOf(typeof(RockInstanceSeed));
}
