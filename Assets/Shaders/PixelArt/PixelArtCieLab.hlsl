#ifndef MEDIEVAL_PIXELART_CIE_LAB_INCLUDED
#define MEDIEVAL_PIXELART_CIE_LAB_INCLUDED

// Linear sRGB (0–1) → CIE Lab (D65)
float3 LinearRgbToCieLab(float3 linearRgb)
{
    float3x3 m = float3x3(
        0.4123908, 0.35758434, 0.18048079,
        0.21263901, 0.71516868, 0.072192315,
        0.01933082, 0.11919478, 0.95053215
    );
    float3 xyz = mul(m, linearRgb);
    const float xn = 0.95047;
    const float yn = 1.00000;
    const float zn = 1.08883;
    const float e = 216.0 / 24389.0;
    const float k = 24389.0 / 27.0;
    float xr = xyz.x / xn;
    float yr = xyz.y / yn;
    float zr = xyz.z / zn;
    float fx = (xr > e) ? pow(xr, 1.0 / 3.0) : (k * xr + 16.0) / 116.0;
    float fy = (yr > e) ? pow(yr, 1.0 / 3.0) : (k * yr + 16.0) / 116.0;
    float fz = (zr > e) ? pow(zr, 1.0 / 3.0) : (k * zr + 16.0) / 116.0;
    float l = 116.0 * fy - 16.0;
    float a = 500.0 * (fx - fy);
    float b = 200.0 * (fy - fz);
    return float3(l, a, b);
}

// Euclidean distance in Lab space
float Cie76Distance(float3 a, float3 b)
{
    return distance(a, b);
}

#endif
