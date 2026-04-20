using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>GPU-packed seed for one rock instance (matches <c>RocksInstance.compute</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RockInstanceSeed
{
    public Vector4 PositionAndYaw;
    public Vector4 ScaleAndPad;

    public static int Stride => Marshal.SizeOf(typeof(RockInstanceSeed));
}
