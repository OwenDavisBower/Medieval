using System;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
#if ANIMATRON_AFFINE_TRANSFORM
using AnyTransform = Unity.Mathematics.AffineTransform;
#elif ANIMATRON_LOCAL_TRANSFORM
using AnyTransform = Unity.Transforms.LocalTransform;
#else
using AnyTransform = ProjectDawn.Animation.RigidTransform;
#endif

namespace ProjectDawn.Animation.Editor
{
    internal struct AnimationSampler
    {
        const float ScaleError = 0.001f;
        public string JointName;
        public AnimationCurves Curves;
        public float3 Position;
        public quaternion Rotation;
        public float3 Scale;

        public AnyTransform Evaluate(float time)
        {
            float3 localPosition;
            localPosition.x = Curves.LocalPositionX?.Evaluate(time) ?? Position.x;
            localPosition.y = Curves.LocalPositionY?.Evaluate(time) ?? Position.y;
            localPosition.z = Curves.LocalPositionZ?.Evaluate(time) ?? Position.z;

            quaternion localRotation;
            if (Curves.Rotation == AnimationCurves.RotationType.Euler)
            {
                // Convert euler to quaternion
                float3 baseRotation = math.Euler(Rotation);
                localRotation = quaternion.Euler(
                    math.radians(Curves.LocalEulerAnglesRawX?.Evaluate(time) ?? baseRotation.x),
                    math.radians(Curves.LocalEulerAnglesRawY?.Evaluate(time) ?? baseRotation.y),
                    math.radians(Curves.LocalEulerAnglesRawZ?.Evaluate(time) ?? baseRotation.z));
                localRotation = math.normalize(localRotation);
            }
            else if (Curves.Rotation == AnimationCurves.RotationType.Quaternion)
            {
                localRotation.value.x = Curves.LocalRotationX?.Evaluate(time) ?? Rotation.value.x;
                localRotation.value.y = Curves.LocalRotationY?.Evaluate(time) ?? Rotation.value.y;
                localRotation.value.z = Curves.LocalRotationZ?.Evaluate(time) ?? Rotation.value.z;
                localRotation.value.w = Curves.LocalRotationW?.Evaluate(time) ?? Rotation.value.w;
                localRotation = math.normalize(localRotation);
            }
            else
            {
                localRotation = Rotation;
            }

            float3 localScale;
            localScale.x = Curves.LocalScaleX?.Evaluate(time) ?? Scale.x;
            localScale.y = Curves.LocalScaleY?.Evaluate(time) ?? Scale.y;
            localScale.z = Curves.LocalScaleZ?.Evaluate(time) ?? Scale.z;

            // Validate the scale with transform type
#if ANIMATRON_AFFINE_TRANSFORM
#elif ANIMATRON_LOCAL_TRANSFORM
            if (!(math.abs(localScale.x - localScale.y) < ScaleError && math.abs(localScale.y - localScale.z) < ScaleError))
                throw new InvalidOperationException($"Animatron is using Local Transform, but the rig contains an animated non-uniform scale at joint '{JointName}' at time {time}, with value {localScale}. To support non-uniform scale animation, go to Project Settings/Animatron and change the Rig Transform Type to Affine Transform.");
#else
            if (!math.all(math.abs(localScale - 1.0f) < ScaleError))
                throw new InvalidOperationException($"Animatron is using Rig Transform, but the rig contains an animated scale at joint '{JointName}' at time {time}, with value {localScale}. To support uniform scale animation, go to Project Settings/Animatron and change the Rig Transform Type to Local Transform.");
#endif

#if ANIMATRON_AFFINE_TRANSFORM
            return math.AffineTransform(localPosition, localRotation, localScale);
#elif ANIMATRON_LOCAL_TRANSFORM
            return AnyTransform.FromPositionRotationScale(localPosition, localRotation, localScale.x);
#else
            return AnyTransform.FromPositionRotation(localPosition, localRotation);
#endif
        }
    }

    internal class AnimationCurves
    {
        public AnimationCurve LocalPositionX;
        public AnimationCurve LocalPositionY;
        public AnimationCurve LocalPositionZ;

        public AnimationCurve LocalRotationX;
        public AnimationCurve LocalRotationY;
        public AnimationCurve LocalRotationZ;
        public AnimationCurve LocalRotationW;

        public AnimationCurve LocalEulerAnglesRawX;
        public AnimationCurve LocalEulerAnglesRawY;
        public AnimationCurve LocalEulerAnglesRawZ;

        public AnimationCurve LocalScaleX;
        public AnimationCurve LocalScaleY;
        public AnimationCurve LocalScaleZ;

        public enum RotationType
        {
            None,
            Quaternion,
            Euler,
        }

        public RotationType Rotation;

        public void AddCurve(AnimationClip clip, EditorCurveBinding binding)
        {
            switch (binding.propertyName)
            {
                case "MotionT.x":
                case "MotionT.y":
                case "MotionT.z":
                case "MotionQ.x":
                case "MotionQ.y":
                case "MotionQ.z":
                case "MotionQ.w":
                    break;

                case "RootT.x":
                case "RootT.y":
                case "RootT.z":
                case "RootQ.x":
                case "RootQ.y":
                case "RootQ.z":
                case "RootQ.w":
                    break;

                case "m_LocalPosition.x":
                    LocalPositionX = AnimationUtility.GetEditorCurve(clip, binding);
                    break;
                case "m_LocalPosition.y":
                    LocalPositionY = AnimationUtility.GetEditorCurve(clip, binding);
                    break;
                case "m_LocalPosition.z":
                    LocalPositionZ = AnimationUtility.GetEditorCurve(clip, binding);
                    break;

                case "m_LocalRotation.x":
                    SetQuaternionSpace();
                    LocalRotationX = AnimationUtility.GetEditorCurve(clip, binding);
                    break;
                case "m_LocalRotation.y":
                    SetQuaternionSpace();
                    LocalRotationY = AnimationUtility.GetEditorCurve(clip, binding);
                    break;
                case "m_LocalRotation.z":
                    SetQuaternionSpace();
                    LocalRotationZ = AnimationUtility.GetEditorCurve(clip, binding);
                    break;
                case "m_LocalRotation.w":
                    SetQuaternionSpace();
                    LocalRotationW = AnimationUtility.GetEditorCurve(clip, binding);
                    break;

                case "m_LocalScale.x":
                    LocalScaleX = AnimationUtility.GetEditorCurve(clip, binding);
                    break;
                case "m_LocalScale.y":
                    LocalScaleY = AnimationUtility.GetEditorCurve(clip, binding);
                    break;
                case "m_LocalScale.z":
                    LocalScaleZ = AnimationUtility.GetEditorCurve(clip, binding);
                    break;

                case "localEulerAnglesRaw.x":
                    SetEulerSpace();
                    LocalEulerAnglesRawX = AnimationUtility.GetEditorCurve(clip, binding);
                    break;
                case "localEulerAnglesRaw.y":
                    SetEulerSpace();
                    LocalEulerAnglesRawY = AnimationUtility.GetEditorCurve(clip, binding);
                    break;
                case "localEulerAnglesRaw.z":
                    SetEulerSpace();
                    LocalEulerAnglesRawZ = AnimationUtility.GetEditorCurve(clip, binding);
                    break;

                default:
                    throw new System.Exception("Not implemented");
            }
        }

        void SetQuaternionSpace()
        {
            if (Rotation == RotationType.Euler)
                throw new InvalidOperationException("Cannot add a quaternion curve when an Euler curve already exists.");
            Rotation = RotationType.Quaternion;
        }

        void SetEulerSpace()
        {
            if (Rotation == RotationType.Quaternion)
                throw new InvalidOperationException("Cannot add an Euler curve when a quaternion curve already exists.");
            Rotation = RotationType.Euler;
        }
    }
}