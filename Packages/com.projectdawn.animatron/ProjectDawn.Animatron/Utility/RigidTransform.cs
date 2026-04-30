using System;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Properties;

namespace ProjectDawn.Animation
{
    /// <summary>
    /// Position, rotation of this entity
    /// </summary>
    [BurstCompile]
    public struct RigidTransform
    {
        /// <summary>
        /// The position of this transform.
        /// </summary>
        [CreateProperty]
        public float3 Position;

        /// <summary>
        /// The rotation of this transform.
        /// </summary>
        [CreateProperty]
        public quaternion Rotation;

        /// <summary>
        /// The identity transform.
        /// </summary>
        public static readonly RigidTransform Identity = new RigidTransform { Rotation = quaternion.identity };

        /// <summary>
        /// Returns the Transform equivalent of a float4x4 matrix.
        /// </summary>
        /// <param name="matrix">The orthogonal matrix to convert.</param>
        /// <remarks>
        /// If the input matrix contains non-uniform scale, the largest value will be used.
        /// Any shear in the input matrix will be ignored.
        /// </remarks>
        /// <seealso cref="FromMatrixSafe"/>
        /// <returns>The Transform.</returns>
        public static RigidTransform FromMatrix(float4x4 matrix)
        {
            var position = matrix.c3.xyz;
            var scaleX = math.length(matrix.c0.xyz);
            var scaleY = math.length(matrix.c1.xyz);
            var scaleZ = math.length(matrix.c2.xyz);

            float3x3 normalizedRotationMatrix = math.orthonormalize(new float3x3(matrix));
            var rotation = new quaternion(normalizedRotationMatrix);

            var transform = default(RigidTransform);
            transform.Position = position;
            transform.Rotation = rotation;
            return transform;
        }

        /// <summary>
        /// Returns the Transform equivalent of a float4x4 matrix. Throws and exception if the matrix contains
        /// nonuniform scale or shear.
        /// </summary>
        /// <param name="matrix">The orthogonal matrix to convert.</param>
        /// <remarks>
        /// If the input matrix contains non-uniform scale, this will throw an exception.
        /// If the input matrix contains shear, this will throw an exception.
        /// </remarks>
        /// <seealso cref="FromMatrix"/>
        /// <returns>The Transform.</returns>
        public static RigidTransform FromMatrixSafe(float4x4 matrix)
        {
            var tolerance = .001f;
            var tolerancesq = tolerance * tolerance;

            var matrix3x3 = new float3x3(matrix);

            float dot01 = math.dot(matrix3x3.c0, matrix3x3.c1);
            float dot02 = math.dot(matrix3x3.c0, matrix3x3.c2);
            float dot12 = math.dot(matrix3x3.c1, matrix3x3.c2);

            // If the matrix is orthogonal, the combined result should be identity
            if (math.abs(dot01) > tolerancesq ||
                math.abs(dot02) > tolerancesq ||
                math.abs(dot12) > tolerancesq)
            {
                throw new ArgumentException("Trying to convert a float4x4 to a RigidTransform, but the rotation 3x3 is not orthogonal");
            }

            float3x3 normalizedRotationMatrix = math.orthonormalize(matrix3x3);
            var rotation = new quaternion(normalizedRotationMatrix);

            var position = matrix.c3.xyz;

            var transform = default(RigidTransform);
            transform.Position = position;
            transform.Rotation = rotation;
            return transform;
        }


        /// <summary>
        /// Returns a Transform initialized with the given position and rotation.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="rotation">The rotation.</param>
        /// <returns>The Transform.</returns>
        public static RigidTransform FromPositionRotation(float3 position, quaternion rotation) => new() { Position = position, Rotation = rotation };

        /// <summary>
        /// Returns a Transform initialized with the given position. Rotation will be identity.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>The Transform.</returns>
        public static RigidTransform FromPosition(float3 position) => new() { Position = position, Rotation = quaternion.identity };

        /// <summary>
        /// Returns a Transform initialized with the given position. Rotation will be identity.
        /// </summary>
        /// <param name="x">The x coordinate of the position.</param>
        /// <param name="y">The y coordinate of the position.</param>
        /// <param name="z">The z coordinate of the position.</param>
        /// <returns>The Transform.</returns>
        public static RigidTransform FromPosition(float x, float y, float z) => new() { Position = new float3(x, y, z), Rotation = quaternion.identity };

        /// <summary>
        /// Returns a Transform initialized with the given rotation. Position will be 0,0,0.
        /// </summary>
        /// <param name="rotation">The rotation.</param>
        /// <returns>The Transform.</returns>
        public static RigidTransform FromRotation(quaternion rotation) => new() { Position = float3.zero, Rotation = rotation };

        /// <summary>
        /// Convert transformation data to a human-readable string
        /// </summary>
        /// <returns>The transform value as a human-readable string</returns>
        public override string ToString()
        {
            return $"Position={Position.ToString()} Rotation={Rotation.ToString()}";
        }

        /// <summary>
        /// Gets the right vector of unit length.
        /// </summary>
        /// <returns>The right vector.</returns>
        public float3 Right() => TransformDirection(math.right());

        /// <summary>
        /// Gets the up vector of unit length.
        /// </summary>
        /// <returns>The up vector.</returns>
        public float3 Up() => TransformDirection(math.up());

        /// <summary>
        /// Gets the forward vector of unit length.
        /// </summary>
        /// <returns>The forward vector.</returns>
        public float3 Forward() => TransformDirection(math.forward());

        /// <summary>
        /// Transforms a point by this transform.
        /// </summary>
        /// <param name="point">The point to be transformed.</param>
        /// <returns>The point after transformation.</returns>
        public float3 TransformPoint(float3 point) => Position + math.rotate(Rotation, point);

        /// <summary>
        /// Transforms a point by the inverse of this transform.
        /// </summary>
        /// <remarks>
        /// Throws if the <see cref="Scale"/> field is zero.
        /// </remarks>
        /// <param name="point">The point to be transformed.</param>
        /// <returns>The point after transformation.</returns>
        public float3 InverseTransformPoint(float3 point) => math.rotate(math.conjugate(Rotation), point - Position);

        /// <summary>
        /// Transforms a direction by this transform.
        /// </summary>
        /// <param name="direction">The direction to be transformed.</param>
        /// <returns>The direction after transformation.</returns>
        public float3 TransformDirection(float3 direction) => math.rotate(Rotation, direction);

        /// <summary>
        /// Transforms a direction by the inverse of this transform.
        /// </summary>
        /// <param name="direction">The direction to be transformed.</param>
        /// <returns>The direction after transformation.</returns>
        public float3 InverseTransformDirection(float3 direction) => math.rotate(math.conjugate(Rotation), direction);

        /// <summary>
        /// Transforms a rotation by this transform.
        /// </summary>
        /// <param name="rotation">The rotation to be transformed.</param>
        /// <returns>The rotation after transformation.</returns>
        public quaternion TransformRotation(quaternion rotation) => math.mul(Rotation, rotation);

        /// <summary>
        /// Transforms a rotation by the inverse of this transform.
        /// </summary>
        /// <param name="rotation">The rotation to be transformed.</param>
        /// <returns>The rotation after transformation.</returns>
        public quaternion InverseTransformRotation(quaternion rotation) => math.mul(math.conjugate(Rotation), rotation);

        /// <summary>
        /// Transforms a Transform by this transform.
        /// </summary>
        /// <param name="transformData">The Transform to be transformed.</param>
        /// <returns>The Transform after transformation.</returns>
        public RigidTransform TransformTransform(in RigidTransform transformData) => new()
        {
            Position = TransformPoint(transformData.Position),
            Rotation = TransformRotation(transformData.Rotation),
        };

        /// <summary>
        /// Transforms a <see cref="RigidTransform"/> by the inverse of this transform.
        /// </summary>
        /// <param name="transformData">The <see cref="RigidTransform"/> to be transformed.</param>
        /// <returns>The <see cref="RigidTransform"/> after transformation.</returns>
        public RigidTransform InverseTransformTransform(in RigidTransform transformData) => new()
        {
            Position = InverseTransformPoint(transformData.Position),
            Rotation = InverseTransformRotation(transformData.Rotation),
        };

        /// <summary>
        /// Gets the inverse of this transform.
        /// </summary>
        /// <remarks>
        /// This method will throw if the <see cref="Scale"/> field is zero.
        /// </remarks>
        /// <returns>The inverse of the transform.</returns>
        public RigidTransform Inverse()
        {
            var inverseRotation = math.conjugate(Rotation);
            return new()
            {
                Position = -math.rotate(inverseRotation, Position),
                Rotation = inverseRotation,
            };
        }

        /// <summary>
        /// Gets the float4x4 equivalent of this transform.
        /// </summary>
        /// <returns>The float4x4 matrix.</returns>
        public float4x4 ToMatrix() => float4x4.TRS(Position, Rotation, 1.0f);

        /// <summary>
        /// Gets the float4x4 equivalent of the inverse of this transform.
        /// </summary>
        /// <returns>The inverse float4x4 matrix.</returns>
        public float4x4 ToInverseMatrix() => Inverse().ToMatrix();

        /// <summary>
        /// Gets an identical transform with a new position value.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>The transform.</returns>
        public RigidTransform WithPosition(float3 position) => new() { Position = position, Rotation = Rotation };

        /// <summary>
        /// Creates a transform that is identical but with a new position value.
        /// </summary>
        /// <param name="x">The x coordinate of the new position.</param>
        /// <param name="y">The y coordinate of the new position.</param>
        /// <param name="z">The z coordinate of the new position.</param>
        /// <returns>The new transform.</returns>
        public RigidTransform WithPosition(float x, float y, float z) => new() { Position = new float3(x, y, z), Rotation = Rotation };

        /// <summary>
        /// Gets an identical transform with a new rotation value.
        /// </summary>
        /// <param name="rotation">The rotation.</param>
        /// <returns>The transform.</returns>
        public RigidTransform WithRotation(quaternion rotation) => new() { Position = Position, Rotation = rotation };

        /// <summary>
        /// Translates this transform by the specified vector.
        /// </summary>
        /// <remarks>
        /// Note that this doesn't modify the original transform. Rather it returns a new one.
        /// </remarks>
        /// <param name="translation">The translation vector.</param>
        /// <returns>A new, translated Transform.</returns>
        public RigidTransform Translate(float3 translation) => new() { Position = Position + translation, Rotation = Rotation };

        /// <summary>
        /// Scales this transform by the specified factor.
        /// </summary>
        /// <remarks>
        /// Note that this doesn't modify the original transform. Rather it returns a new one.
        /// </remarks>
        /// <param name="scale">The scaling factor.</param>
        /// <returns>A new, scaled Transform.</returns>
        public RigidTransform ApplyScale(float scale) => new() { Position = Position, Rotation = Rotation };

        /// <summary>
        /// Rotates this Transform by the specified quaternion.
        /// </summary>
        /// <remarks>
        /// Note that this doesn't modify the original transform. Rather it returns a new one.
        /// </remarks>
        /// <param name="rotation">The rotation quaternion of unit length.</param>
        /// <returns>A new, rotated Transform.</returns>
        public RigidTransform Rotate(quaternion rotation) => new() { Position = Position, Rotation = math.mul(Rotation, rotation) };

        /// <summary>
        /// Rotates this Transform around the X axis.
        /// </summary>
        /// <remarks>
        /// Note that this doesn't modify the original transform. Rather it returns a new one.
        /// </remarks>
        /// <param name="angle">The X rotation.</param>
        /// <returns>A new, rotated Transform.</returns>
        public RigidTransform RotateX(float angle) => Rotate(quaternion.RotateX(angle));

        /// <summary>
        /// Rotates this Transform around the Y axis.
        /// </summary>
        /// <remarks>
        /// Note that this doesn't modify the original transform. Rather it returns a new one.
        /// </remarks>
        /// <param name="angle">The Y rotation.</param>
        /// <returns>A new, rotated Transform.</returns>
        public RigidTransform RotateY(float angle) => Rotate(quaternion.RotateY(angle));

        /// <summary>
        /// Rotates this Transform around the Z axis.
        /// </summary>
        /// <remarks>
        /// Note that this doesn't modify the original transform. Rather it returns a new one.
        /// </remarks>
        /// <param name="angle">The Z rotation.</param>
        /// <returns>A new, rotated Transform.</returns>
        public RigidTransform RotateZ(float angle) => Rotate(quaternion.RotateZ(angle));

        /// <summary>Checks if a transform has equal position, rotation, and scale to another.</summary>
        /// <param name="other">The TransformData to compare.</param>
        /// <returns>Returns true if the position, rotation, and scale are equal.</returns>
        public bool Equals(in RigidTransform other)
        {
            return Position.Equals(other.Position) && Rotation.Equals(other.Rotation);
        }
    }
}