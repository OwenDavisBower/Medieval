using Unity.Mathematics;
using System.Runtime.CompilerServices;
using System;
using System.Diagnostics;

namespace ProjectDawn.Animation
{
    /// <summary>
    /// Single float3 inertia data.
    /// Based on https://www.gdcvault.com/play/1025331/Inertialization-High-Performance-Animation-Transitions.
    /// </summary>
    public struct Float3Inertia
    {
        public InertializationCoefficientsOptimized Magnitude;
        public float3 Axis;

        public float3 Evaluate(in TimePower t)
        {
            float magnitude = Magnitude.Evaluate(t);
            return magnitude * Axis;
        }

        /// <summary>
        /// Pre-computes kofficients of inertia solver.
        /// </summary>
        public static Float3Inertia Create(float3 previous, float3 source, float3 target, float deltaTime, float duration)
        {
            var translation = source - target;
            var magnitude = math.length(translation);

            var direction = math.normalizesafe(translation);

            var previousTranslation = previous - target;
            var previousMagnitude = math.dot(previousTranslation, direction);

            var velocity = deltaTime != 0 ? (magnitude - previousMagnitude) / deltaTime : 0;

            return new Float3Inertia
            {
                Magnitude = InertializationCoefficientsOptimized.Create(magnitude, velocity, duration),
                Axis = direction,
            };
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CheckValidity(float3 source, float3 target)
        {
            var timePower = new TimePower(0);
            var source2 = target + Evaluate(timePower);
            float error = math.distancesq(source, source2);
            if (error >= 0.01f)
                throw new InvalidOperationException("Position inertia failed!");
        }
    }

    /// <summary>
    /// Single quaternion inertia data.
    /// Based on https://www.gdcvault.com/play/1025331/Inertialization-High-Performance-Animation-Transitions.
    /// </summary>
    public struct QuaternionInertia
    {
        const float EpsilonNormalSqrt = 1e-15f;

        public InertializationCoefficientsOptimized Angle;
        public float3 Axis;

        public quaternion Evaluate(in TimePower t)
        {
            float angle = Angle.Evaluate(t);
            return quaternion.AxisAngle(Axis, angle);
        }

        /// <summary>
        /// Pre-computes kofficients of inertia solver.
        /// </summary>
        public static QuaternionInertia Create(quaternion previous, quaternion source, quaternion target, float deltaTime, float duration)
        {
            var inverseTarget = math.inverse(target);

            var rotation = math.normalize(math.mul(source, inverseTarget));
            var (axis, angle) = ToAxisAngle(rotation);

            // Ensure that rotations are the shortest possible
            if (angle > math.PI)
            {
                angle = math.PI2 - angle;
                axis = -axis;
            }

            var previousRotation = math.mul(previous, inverseTarget);
            var previousAngle = UnwindOnce(2.0f * math.atan2(math.dot(previousRotation.value.xyz, axis), previousRotation.value.w));

            var velocity = UnwindOnce(deltaTime != 0 ? (angle - previousAngle) / deltaTime : 0);

            return new QuaternionInertia
            {
                Angle = InertializationCoefficientsOptimized.Create(angle, velocity, duration),
                Axis = axis,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (float3 axis, float angle) ToAxisAngle(quaternion q)
        {
            float denom = math.sqrt(1f - q.value.w * q.value.w);
            return (math.select(q.value.xyz * math.rcp(denom), math.float3(1f, 0f, 0f), math.abs(denom) < EpsilonNormalSqrt), 2f * math.acos(math.clamp(q.value.w, -1f, 1f)));
        }

        /// <summary>
        /// Unwind angle (in radians) to be between [-PI, PI] range. Only works if the angle is already in the [-3PI, 3PI] range.
        /// </summary>
        /// <param name="a">angle in radians, must be in the [-3PI, 3PI] range</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float UnwindOnce(float a)
        {
            if (a < -math.PI)
                a += math.PI2;
            if (a > math.PI)
                a -= math.PI2;
            return a;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CheckValidity(quaternion source, quaternion target)
        {
            var timePower = new TimePower(0);
            var source2 = math.normalize(math.mul(Evaluate(timePower), target));
            float error = math.distancesq(math.rotate(source2.value, math.right()), math.rotate(source.value, math.right()));
            if (error >= 0.01f)
                throw new InvalidOperationException("Rotation inertia failed!");
        }
    }

    public struct TimePower
    {
        public float Value;
        public float4 Powers;

        public TimePower(float time)
        {
            Value = time;
            Powers.x = Value * time;
            Powers.y = Powers.x * time;
            Powers.z = Powers.y * time;
            Powers.w = Powers.z * time;
        }
    }

    /// <summary>
    /// Single float inertia data.
    /// Based on https://www.gdcvault.com/play/1025331/Inertialization-High-Performance-Animation-Transitions.
    /// </summary>
    public struct InertializationCoefficients
    {
        public float x0;
        public float v0;
        public float a0;
        public float A;
        public float B;
        public float C;
        public float t1;

        public float Evaluate(in TimePower t)
        {
            if (t.Value >= t1)
                return 0;

            return A * t.Powers.w + B * t.Powers.z + C * t.Powers.y + (a0 / 2) * t.Powers.x + v0 * t.Value + x0;
        }

        /// <summary>
        /// Pre-computes kofficients of inertia solver.
        /// </summary>
        public static InertializationCoefficients Create(float x0, float v0, float t1)
        {
            // Modify t1 to account for overshoot
            // Here is small modification instead of:
            // t1 = math.min(t1, -5 * x0 / v0);
            // In case of missmatch sign of x0 and v0 it produces negative time
            if (math.abs(v0) > math.EPSILON)
                t1 = math.min(t1, math.abs(5 * x0 / v0));

            float t2 = t1 * t1;
            float t3 = t2 * t1;
            float t4 = t3 * t1;
            float t5 = t4 * t1;

            float a0 = (-8 * v0 * t1 - 20 * x0) / t2;

            var A = -(a0 * t2 + 6 * v0 * t1 + 12 * x0) / (2 * t5);
            var B = (3 * a0 * t2 + 16 * v0 * t1 + 30 * x0) / (2 * t4);
            var C = -(3 * a0 * t2 + 12 * v0 * t1 + 20 * x0) / (2 * t3);

            return new InertializationCoefficients
            {
                x0 = x0,
                v0 = v0,
                a0 = a0,
                A = A,
                B = B,
                C = C,
                t1 = t1,
            };
        }
    }

    /// <summary>
    /// Single float inertia data.
    /// Based on https://www.gdcvault.com/play/1025331/Inertialization-High-Performance-Animation-Transitions.
    /// </summary>
    public struct InertializationCoefficientsOptimized
    {
        public float4 DCBA;
        public float x0;
        public float v0;
        public float t1;

        readonly static float4x3 Constant = new float4x3(
            new float4(0, -3, 3, -1),
            new float4(-4, -6, 8, -3),
            new float4(-10, -10, 15, -6));

        public float Evaluate(in TimePower t)
        {
            if (t.Value >= t1)
                return 0;
            return math.dot(DCBA, t.Powers) + v0 * t.Value + x0;
        }

        /// <summary>
        /// Pre-computes kofficients of inertia solver.
        /// </summary>
        public static InertializationCoefficientsOptimized Create(float x0, float v0, float t1)
        {
            // Modify t1 to account for overshoot
            // Here is small modification instead of:
            // t1 = math.min(t1, -5 * x0 / v0);
            // In case of missmatch sign of x0 and v0 it produces negative time
            if (math.abs(v0) > math.EPSILON)
                t1 = math.min(t1, math.abs(5 * x0 / v0));

            float v0t1 = v0 * t1;
            float a0 = (-4 * v0t1 - 10 * x0);

            float4 numerator = math.mul(Constant, new float3(a0, v0t1, x0));

            float4 denominator;
            denominator.x = t1 * t1;
            denominator.y = denominator.x * t1;
            denominator.z = denominator.y * t1;
            denominator.w = denominator.z * t1;

            return new InertializationCoefficientsOptimized
            {
                DCBA = numerator / denominator,
                x0 = x0,
                v0 = v0,
                t1 = t1,
            };
        }
    }
}
