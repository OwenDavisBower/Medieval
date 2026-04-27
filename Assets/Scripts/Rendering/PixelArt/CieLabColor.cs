using UnityEngine;

namespace Medieval.Rendering.PixelArt
{
    /// <summary>
    /// CIE Lab (D65) for palette precomputation in C# to match the shader.
    /// </summary>
    public static class CieLabColor
    {
        const float Epsilon = 216f / 24389f;
        const float Kappa = 24389f / 27f;

        public static Vector3 LinearRgbToLab(Color linearRgb)
        {
            float r = linearRgb.r;
            float g = linearRgb.g;
            float b = linearRgb.b;
            float x = 0.4123908f * r + 0.35758434f * g + 0.18048079f * b;
            float y = 0.21263901f * r + 0.71516868f * g + 0.072192315f * b;
            float z = 0.01933082f * r + 0.11919478f * g + 0.95053215f * b;
            const float xn = 0.95047f;
            const float yn = 1.00000f;
            const float zn = 1.08883f;
            float fx = LabF(x / xn);
            float fy = LabF(y / yn);
            float fz = LabF(z / zn);
            return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        static float LabF(float t)
        {
            if (t > Epsilon)
                return Mathf.Pow(t, 1f / 3f);
            return (Kappa * t + 16f) / 116f;
        }
    }
}
