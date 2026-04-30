using System;
using Unity.Collections;
using UnityEditor.AssetImporters;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using UnityEditor;
using Unity.Entities;
using System.Runtime.CompilerServices;
using ProjectDawn.Rendering;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.InteropServices;
#if ANIMATRON_AFFINE_TRANSFORM
using AnyTransform = Unity.Mathematics.AffineTransform;
#elif ANIMATRON_LOCAL_TRANSFORM
using AnyTransform = Unity.Transforms.LocalTransform;
#else
using AnyTransform = ProjectDawn.Animation.RigidTransform;
#endif

namespace ProjectDawn.Animation.Editor
{
    [ScriptedImporter(Rig.DefaultVersion, "rig")]
    public unsafe class RigImporter : ScriptedImporter
    {
        /// <summary>
        /// The prefab from which the <see cref="Rig"/> will be generated.
        /// </summary>
        [Tooltip("The prefab used to generate the rig.")]
        public GameObject Prefab;

        /// <summary>
        /// A list of animations to bake into the rig's <see cref="Motion"/>.
        /// </summary>
        [Tooltip("List of animations to bake into the rig's motion.")]
        public List<Animation> Animations;

        /// <summary>
        /// If enabled, animation names will be embedded in the rig's <see cref="Motion"/>.
        /// </summary>
        [Tooltip("Include animation names in the baked motion if enabled.")]
        public bool AnimationNames = true;

        /// <summary>
        /// A list of skins to be baked into the rig's <see cref="Armature"/>.
        /// A skin is a subset of joints used for mesh skinning.
        /// </summary>
        [Tooltip("Skins to bake into the rig. A skin is a subset of joints used for skinning meshes.")]
        public List<Skin> Skins;

        /// <summary>
        /// If enabled, ensures all meshes use exactly 4 blend weights by regenerating those that don't.
        /// </summary>
        [Tooltip("Regenerates meshes with exactly 4 blend weights if they originally have fewer.")]
        public bool Force4BlendWeights = true;

        /// <summary>
        /// A list of joints to include in the rig's <see cref="Armature"/>.
        /// </summary>
        [Tooltip("Joints to bake into the rig's armature.")]
        public List<Joint> Joints;

        /// <summary>
        /// If enabled, joint names will be embedded in the rig's <see cref="Armature"/>.
        /// </summary>
        [Tooltip("Include joint names in the baked armature if enabled.")]
        public bool JointNames = true;

        /// <summary>
        /// Controls prefab generation.
        /// </summary>
        [Tooltip("Controls prefab generation.")]
        public GeneratePrefabMode PrefabMode = GeneratePrefabMode.RenderMeshArray;

        [System.Serializable]
        public class Animation
        {
            /// <summary>
            /// The name identifier for the animation.
            /// </summary>
            [Tooltip("The name identifier for this animation.")]
            public string Name = "Unknown";

            /// <summary>
            /// The animation clip to bake into the rig.
            /// </summary>
            [Tooltip("Animation clip to bake into the rig.")]
            public AnimationClip Clip;

            /// <summary>
            /// The playback speed of the animation.
            /// </summary>
            [Tooltip("Playback speed of the animation.")]
            public float Speed = 1.0f;

            /// <summary>
            /// Should apply foot IK during animation baking. This only works for humanoid animations.
            /// </summary>
            [Tooltip("Should apply foot IK during animation baking. This only works for humanoid animations.")]
            public bool ApplyFootIK = true;

            public List<Event> Events = new();
        }

        [System.Serializable]
        public class Event
        {
            public string TypeName;

            public int Offset;
            public int Length;

            [SerializeReference]
            public IEventData Data;
        }

        [System.Serializable]
        public class Joint
        {
            public bool Enabled;
            public Transform Transform;
        }

        [System.Serializable]
        public class Skin
        {
            public bool Enabled;
            public SkinnedMeshRenderer SkinnedMeshRenderer;
        }

        /// <summary>
        /// Controls prefab generation.
        /// </summary>
        [System.Serializable]
        public enum GeneratePrefabMode
        {
            [Tooltip("No prefab will be generated.")]
            None,
            [Tooltip("Prefab with render mesh array will be generated.")]
            RenderMeshArray,
            [Tooltip("Prefab with skinned mesh renderers will be generated.")]
            SkinnedMeshRenderer,
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            using var armatureBuilder = new BlobBuilder(Allocator.Temp);
            using var motionBuilder = new BlobBuilder(Allocator.Temp);

            if (Prefab == null)
                return;

            Rig asset = ScriptableObject.CreateInstance<Rig>();
            asset.name = Prefab.name;

            // Gather here all enabled joints
            var enabledJoints = new List<Transform>();
            for (int i = 0; i < Joints.Count; i++)
            {
                if (!Joints[i].Enabled)
                    continue;
                enabledJoints.Add(Joints[i].Transform);
            }

            var jointCount = enabledJoints.Count;

            // Bakes armature that will contain most of the information about the joints
            ref var armature = ref armatureBuilder.ConstructRoot<Armature>();
            var enabledRenderers = new List<SkinnedMeshRenderer>();
            {
                // Gather here all enabled renderers
                for (int i = 0; i < Skins.Count; i++)
                {
                    if (!Skins[i].Enabled)
                        continue;

                    enabledRenderers.Add(Skins[i].SkinnedMeshRenderer);
                }

                // Create the skins
                var skins = armatureBuilder.Allocate(ref armature.Skins, enabledRenderers.Count);
                int bindCount = 0;
                for (int skinIndex = 0; skinIndex < enabledRenderers.Count; skinIndex++)
                {
                    var renderer = enabledRenderers[skinIndex];
                    int bindposeCount = enabledRenderers[skinIndex].sharedMesh.bindposeCount;
                    if (!TryFindBindPoseIndex(enabledJoints, enabledRenderers[skinIndex].rootBone, out int rootBindPoseIndex))
                        throw new InvalidOperationException($"Failed to find root joint {enabledRenderers[skinIndex].rootBone} for skin {renderer}");

                    skins[skinIndex] = new ProjectDawn.Animation.Skin
                    {
                        Root = rootBindPoseIndex,
                        Begin = bindCount,
                        End = bindCount + bindposeCount,
                        Bounds = new AABB { Center = renderer.localBounds.center, Extents = renderer.localBounds.extents },
                    };
                    bindCount += bindposeCount;
                }

                // Create the skins bind poses and indices map
                int bindIndex = 0;
                var jointBindPose = armatureBuilder.Allocate(ref armature.SkinBindPoses, bindCount);
                var skinMatrixIndices = armatureBuilder.Allocate(ref armature.SkinJointIndices, bindCount);
                for (int skinIndex = 0; skinIndex < enabledRenderers.Count; skinIndex++)
                {
                    var renderer = enabledRenderers[skinIndex];
                    var bones = renderer.bones;
                    var bindposes = renderer.sharedMesh.bindposes;
                    for (var i = 0; i < bindposes.Length; i++)
                    {
                        var bone = bones[i];

                        if (TryFindBindPoseIndex(enabledJoints, bone, out int bindPoseIndex))
                        {
                            jointBindPose[bindIndex] = bindposes[i];
                            skinMatrixIndices[bindIndex] = bindPoseIndex;
                        }
                        else
                        {
                            var transform = bone.parent;
                            while (transform != null)
                            {
                                if (TryFindBindPoseIndex(enabledJoints, transform, out bindPoseIndex))
                                {
                                    ctx.LogImportWarning($"Failed to joint {bone} need for skin {renderer}", bone);
                                    jointBindPose[bindIndex] = bindposes[FindBone(bones, transform)];
                                    skinMatrixIndices[bindIndex] = bindPoseIndex;
                                    break;
                                }

                                transform = transform.parent;
                            }

                            if (transform == null)
                                throw new InvalidOperationException($"Failed to joint {bone} need for skin {renderer}");
                            //skinMatrixIndices[bindIndex] = -1;
                        }

                        bindIndex++;
                    }
                }

                // Bakes joints parent index array that will be used finding the index of joint parent
                var parentIndices = armatureBuilder.Allocate(ref armature.JointParentIndices, jointCount);
                for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
                {
                    parentIndices[jointIndex] = FindBone(enabledJoints, enabledJoints[jointIndex].parent);
                }

                // Bakes joint names
                if (JointNames)
                {
                    var jointNames = armatureBuilder.Allocate(ref armature.JointNames, jointCount);
                    for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
                    {
                        jointNames[jointIndex] = enabledJoints[jointIndex].name;
                    }
                }
                else
                {
                    armatureBuilder.Allocate(ref armature.JointNames, 0);
                }

                asset.WriteArmature(armatureBuilder);
            }

            // Bakes motion that contains all animation data
            ref var motion = ref motionBuilder.ConstructRoot<Motion>();
            var allAnimations = new List<Animation>();
            {
                // Here we insert t-pose animation to index 0
                // This is done for convinience purpose as index 0 will act as non valid animation 
                allAnimations.Add(new Animation
                {
                    Name = "",
                });
                allAnimations.AddRange(this.Animations);

                // Bakes the animations
                motion.JointCount = jointCount;
                motion.PoseCount = 0;
                motion.PoseStride = jointCount * sizeof(AnyTransform);
                var animations = motionBuilder.Allocate(ref motion.Animations, allAnimations.Count);
                for (var clipIndex = 0; clipIndex < allAnimations.Count; clipIndex++)
                {
                    var animation = allAnimations[clipIndex];
                    var clip = animation.Clip;
                    if (clip != null)
                    {
                        var bindings = AnimationUtility.GetCurveBindings(clip);
                        var clipPoseCount = (int)(clip.length * clip.frameRate);

                        animations[clipIndex] = new Motion.Animation
                        {
                            Begin = motion.PoseCount,
                            End = motion.PoseCount + clipPoseCount,
                            FrameRate = clip.frameRate,
                            IsLooping = clip.isLooping,
                            Speed = animation.Speed,
                        };
                        motion.PoseCount += clipPoseCount;
                    }
                    else
                    {
                        animations[clipIndex] = new Motion.Animation
                        {
                            Begin = motion.PoseCount,
                            End = motion.PoseCount + 1,
                            FrameRate = 1,
                            Speed = animation.Speed,
                        };
                        motion.PoseCount += 1;
                    }
                }

                // Bakes animation names
                if (AnimationNames)
                {
                    var animtionNames = motionBuilder.Allocate(ref motion.AnimationNames, allAnimations.Count);
                    for (var clipIndex = 0; clipIndex < allAnimations.Count; clipIndex++)
                    {
                        var animation = allAnimations[clipIndex];
                        animtionNames[clipIndex] = animation.Name;
                    }
                }
                else
                {
                    motionBuilder.Allocate(ref motion.AnimationNames, 0);
                }

                // Bakes animation poses
                // TODO: This is the slowest part? Burst it?
                UnityEngine.Profiling.Profiler.BeginSample("BuildPoses");
                var transforms = motionBuilder.Allocate(ref motion.Transforms, motion.PoseCount * jointCount);
                for (var clipIndex = 0; clipIndex < allAnimations.Count; clipIndex++)
                {
                    var animationManaged = allAnimations[clipIndex];
                    var clip = animationManaged.Clip;

                    float sampleRate = 1.0f / animations[clipIndex].FrameRate;
                    var animation = animations[clipIndex];

                    if (clip && clip.isHumanMotion) // Humanoid animation clips
                    {
                        var copy = GameObject.Instantiate(Prefab);
                        var animator = copy.GetComponent<Animator>();

                        // Check is avatar is valid for humanoid sampling
                        Avatar avatar = animator.avatar;
                        if (avatar == null)
                            throw new InvalidOperationException($"Prefab {Prefab} must contain avatar.");
                        if (!avatar.isValid || !avatar.isHuman)
                            throw new InvalidOperationException($"Animation clip {clip} uses humanoid, but the prefab is not humanoid.");

                        // This is required, otherwise first few frames it will not update
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                        // Setup PlayableGraph
                        var graph = PlayableGraph.Create("HumanPoseSampling");
                        var output = AnimationPlayableOutput.Create(graph, "Output", animator);
                        var playable = AnimationClipPlayable.Create(graph, clip);
                        playable.SetApplyFootIK(animationManaged.ApplyFootIK);
                        output.SetSourcePlayable(playable);

                        for (int poseIndex = animation.Begin; poseIndex < animation.End; ++poseIndex)
                        {
                            // Sets pose at specific index
                            float time = (poseIndex - animation.Begin) * sampleRate;
                            playable.SetTime(time);
                            graph.Evaluate();

                            // Loop through each joint in your rig
                            for (int jointIndex = 0; jointIndex < enabledJoints.Count; jointIndex++)
                            {
                                int transformIndex = motion.GetJointTransformIndex(jointIndex, poseIndex);
                                var jointPath = GetBonePath(Prefab.transform, enabledJoints[jointIndex]);

                                foreach (var transform in animator.GetComponentsInChildren<Transform>())
                                {
                                    var childJointPath = GetBonePath(copy.transform, transform);

                                    if (jointPath != childJointPath)
                                        continue;

                                    var localPosition = transform.localPosition;
                                    var localRotation = transform.localRotation;
                                    var localScale = transform.localScale;

#if ANIMATRON_AFFINE_TRANSFORM
                                    transforms[transformIndex] =  math.AffineTransform(localPosition, localRotation, localScale);
#elif ANIMATRON_LOCAL_TRANSFORM
                                    transforms[transformIndex] =  AnyTransform.FromPositionRotationScale(localPosition, localRotation, localScale.x);
#else
                                    transforms[transformIndex] = AnyTransform.FromPositionRotation(localPosition, localRotation);
#endif

                                    break;
                                }
                            }
                        }

                        GameObject.DestroyImmediate(copy);

                        graph.Destroy();

                        continue;
                    }
                    else // Legacy and Generic animation clips
                    {
                        // Here we extract the animation curves from the clip
                        // Also we created here binding map from joint path to binding this improves performance quite a lot
                        var bindings = clip ? AnimationUtility.GetCurveBindings(clip) : Array.Empty<EditorCurveBinding>();
                        var bindingMap = new Dictionary<string, AnimationCurves>();
                        foreach (var binding in bindings)
                        {
                            if (!bindingMap.TryGetValue(binding.path, out AnimationCurves curves))
                            {
                                curves = new AnimationCurves();
                                bindingMap.Add(binding.path, curves);
                            }

                            curves.AddCurve(clip, binding);
                        }

                        string rootPath = clip ? bindings[0].path : "";

                        for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
                        {
                            // Build joint path which will be used to find binding
                            // TODO: This needs improvement
                            var jointPath = GetBonePath(Prefab.transform, enabledJoints[jointIndex]);
                            int jointPathOffset = jointPath.IndexOf(rootPath);
                            if (jointPathOffset != -1)
                                jointPath = jointPath.Substring(jointPathOffset);
                            if (!bindingMap.TryGetValue(jointPath, out var curves))
                                curves = new AnimationCurves();

                            var sampler = new AnimationSampler()
                            {
                                JointName = jointPath,
                                Curves = curves,
                                Position = enabledJoints[jointIndex].localPosition,
                                Rotation = enabledJoints[jointIndex].localRotation,
                                Scale = enabledJoints[jointIndex].localScale,
                            };

                            for (int poseIndex = animation.Begin; poseIndex < animation.End; ++poseIndex)
                            {
                                float time = (poseIndex - animation.Begin) * sampleRate;
                                int transformIndex = motion.GetJointTransformIndex(jointIndex, poseIndex);
                                transforms[transformIndex] = sampler.Evaluate(time);
                            }
                        }
                    }
                }
                UnityEngine.Profiling.Profiler.EndSample();

                // Bake events
                {
                    NativeList<byte> collectedEventData = new NativeList<byte>(Allocator.Temp);
                    BlobBuilderArray<Motion.Event> events = default;
                    for (var clipIndex = 0; clipIndex < allAnimations.Count; clipIndex++)
                    {
                        var animation = allAnimations[clipIndex];
                        var clip = animation.Clip;
                        if (clip == null)
                            continue;

                        // Lazy init events memory on demand as it might not be needed
                        if (events.Length == 0)
                        {
                            events = motionBuilder.Allocate(ref motion.Events, motion.PoseCount * jointCount);
                            UnsafeUtility.MemClear(events.GetUnsafePtr(), sizeof(Motion.Event) * events.Length);
                        }

                        for (var eventIndex = 0; eventIndex < animation.Events.Count; eventIndex++)
                        {
                            var e = animation.Events[eventIndex];

                            if (!EventTypeManager.TryGetTypeFromName(e.TypeName, out Type type))
                            {
                                ctx.LogImportWarning($"Failed to find event with type name {e.TypeName}.");
                                continue;
                            }

                            ComponentType componentType = type;
                            TypeManager.TypeInfo typeInfo = TypeManager.GetTypeInfo(componentType.TypeIndex);

                            int2 data = 0;
                            if (typeInfo.SizeInChunk != 0)
                            {
                                var offset = collectedEventData.Length;
                                collectedEventData.Length += typeInfo.SizeInChunk;

                                var handle = GCHandle.Alloc(e.Data, GCHandleType.Pinned);
                                void* ptr = (void*)handle.AddrOfPinnedObject();
                                UnsafeUtility.MemCpy(
                                    collectedEventData.GetUnsafePtr() + offset,
                                    ptr,
                                    typeInfo.SizeInChunk
                                );
                                handle.Free();

                                data = new int2(offset, typeInfo.SizeInChunk);
                            }

                            int eventSize = UnsafeUtility.SizeOf<Motion.Event>();

                            int poseBegin = math.clamp(animations[clipIndex].Begin + e.Offset, animations[clipIndex].Begin, animations[clipIndex].End);
                            int poseEnd = math.clamp(animations[clipIndex].Begin + e.Offset + e.Length, animations[clipIndex].Begin, animations[clipIndex].End);

                            for (var poseIndex = poseBegin; poseIndex < poseEnd; poseIndex++)
                            {
                                unsafe // It is more performance efficient to get pointer here as we will be reading/writing to it
                                {
                                    var e2 = (Motion.Event*)events.GetUnsafePtr() + poseIndex;

                                    if (e2->IsValid)
                                    {
                                        ctx.LogImportWarning($"Failed to add event at clip {clip.name} and frame {e.Offset + poseIndex}, as another event already exists there. Currently, only one event per specific frame is supported. If you really need multiple events, please contact me on Discord.");
                                        continue;
                                    }

                                    e2->StableTypeHash = typeInfo.StableTypeHash;
                                    e2->DataOffset = data.x;
                                }
                            }
                        }
                    }

                    // We cant have events array being null
                    if (events.GetUnsafePtr() == null)
                    {
                        events = motionBuilder.Allocate(ref motion.Events, 0);
                    }

                    var eventData = motionBuilder.Allocate(ref motion.EventData, collectedEventData.Length);
                    UnsafeUtility.MemCpy(eventData.GetUnsafePtr(), collectedEventData.GetUnsafePtr(), collectedEventData.Length);

                    collectedEventData.Dispose();
                }

                asset.WriteMotion(motionBuilder);
            }

            if (PrefabMode != GeneratePrefabMode.None)
            {
                ctx.AddObjectToAsset("Rig", asset);

                var parent = new GameObject(Prefab.name);
                parent.SetActive(false);
                parent.transform.localScale = Prefab.transform.localScale;
                var animatron = parent.AddComponent<AnimatronAuthoring>();

                animatron.Rig = asset;

                Material defaultMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    RequiresFlexSkinning() ?
                    "Packages/com.projectdawn.animatron/Materials/LitFlexSkinned.mat" :
                    "Packages/com.projectdawn.animatron/Materials/LitSkinned.mat");

                var skins = new AnimatronAuthoring.Skin[enabledRenderers.Count];

                for (int rendererIndex = 0; rendererIndex < enabledRenderers.Count; rendererIndex++)
                {
                    var renderer = enabledRenderers[rendererIndex];

                    // This feature forces vertices to use 4 blend weights
                    Mesh mesh;
                    if (Force4BlendWeights && renderer.sharedMesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.BlendIndices) != 4)
                    {
                        mesh = CreateMeshWith4BoneWeights(renderer.sharedMesh);
                        ctx.AddObjectToAsset(mesh.name, mesh);
                    }
                    else
                    {
                        mesh = renderer.sharedMesh;
                    }

                    switch (PrefabMode)
                    {
                        case GeneratePrefabMode.RenderMeshArray:
                            {
                                var child = new GameObject(renderer.name, typeof(RenderMeshArrayAuthoring));
                                child.transform.SetParent(parent.transform, false);
                                child.transform.position = renderer.transform.position;
                                child.transform.rotation = renderer.transform.rotation;
                                child.transform.localScale = renderer.transform.localScale;
                                var childRenderer = child.GetComponent<RenderMeshArrayAuthoring>();

                                var instances = new RenderMeshArrayAuthoring.Instance[renderer.sharedMaterials.Length];
                                var materials = new Material[renderer.sharedMaterials.Length];
                                for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                                {
                                    instances[i] = (new RenderMeshArrayAuthoring.Instance
                                    {
                                        Material = defaultMaterial,
                                        Mesh = mesh,
                                        SubMesh = i,
                                    });
                                }
                                childRenderer.SetInstances(instances);

                                childRenderer.Bounds = new AABB { Center = renderer.localBounds.center, Extents = renderer.localBounds.extents };

                                skins[rendererIndex] = new AnimatronAuthoring.Skin
                                {
                                    BindingIndex = rendererIndex,
                                    GameObject = child,
                                };
                            }
                            break;
                        case GeneratePrefabMode.SkinnedMeshRenderer:
                            {
                                var materials = new Material[renderer.sharedMaterials.Length];
                                for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                                    materials[i] = defaultMaterial;

                                var child = new GameObject(renderer.name, typeof(SkinnedMeshRenderer));
                                child.transform.SetParent(parent.transform, false);
                                child.transform.position = renderer.transform.position;
                                child.transform.rotation = renderer.transform.rotation;
                                child.transform.localScale = renderer.transform.localScale;
                                var childRenderer = child.GetComponent<SkinnedMeshRenderer>();
                                childRenderer.sharedMesh = mesh;
                                childRenderer.sharedMaterials = materials;
                                childRenderer.motionVectorGenerationMode = renderer.motionVectorGenerationMode;
                                childRenderer.shadowCastingMode = renderer.shadowCastingMode;
                                childRenderer.rootBone = renderer.rootBone;
                                childRenderer.renderingLayerMask = renderer.renderingLayerMask;

                                skins[rendererIndex] = new AnimatronAuthoring.Skin
                                {
                                    BindingIndex = rendererIndex,
                                    GameObject = child,
                                };
                            }
                            break;
                    }
                }

                animatron.Skins = skins;

                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/com.projectdawn.animatron/Icons/d_PrefabRig@256.psd");

                parent.SetActive(true);
                ctx.AddObjectToAsset(Prefab.name, parent, icon);
                ctx.SetMainObject(parent);
            }
            else
            {
                ctx.AddObjectToAsset("Rig", asset);
                ctx.SetMainObject(asset);
            }

            // As we rebaked new rig clear the previous blob data
            asset.Clear();

            // As the actualy *.rig asset it simply a dummy here we set dependency on prefab
            string prefabPath = AssetDatabase.GetAssetPath(Prefab);
            ctx.DependsOnSourceAsset(prefabPath);
        }

        public bool RequiresFlexSkinning()
        {
            foreach (var renderer in Skins)
            {
                if (!renderer.Enabled)
                    continue;
                if (renderer.SkinnedMeshRenderer.sharedMesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.BlendIndices) != 4)
                    return true;
            }
            return false;
        }

        static Mesh CreateMeshWith4BoneWeights(Mesh original)
        {
            Mesh newMesh = UnityEngine.Object.Instantiate(original);

            newMesh.name = $"{original.name} (4 bones)";

            var boneWeights = original.boneWeights;
            int vertexCount = original.vertexCount;
            var fullBoneWeights = new BoneWeight[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                BoneWeight bw = boneWeights[i];

                // Ensure all 4 weights/dimensions are present
                fullBoneWeights[i] = new BoneWeight
                {
                    boneIndex0 = bw.boneIndex0,
                    boneIndex1 = bw.boneIndex1,
                    boneIndex2 = bw.boneIndex2,
                    boneIndex3 = bw.boneIndex3,
                    weight0 = bw.weight0,
                    weight1 = bw.weight1,
                    weight2 = bw.weight2,
                    weight3 = bw.weight3
                };
            }

            newMesh.boneWeights = fullBoneWeights;
            return newMesh;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float3x4 ToFloat3x4(float4x4 value) => new(value.c0.xyz, value.c1.xyz, value.c2.xyz, value.c3.xyz);

        public void CollectRenderers()
        {
            Skins.Clear();
            if (Prefab == null)
                return;
            var renderers = Prefab.transform.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var renderer in renderers)
                Skins.Add(new Skin { Enabled = true, SkinnedMeshRenderer = renderer });
        }

        public void CollectBones()
        {
            Joints.Clear();
            if (Prefab == null)
                return;
            for (int i = 0; i < Prefab.transform.childCount; i++)
                BuildArmatureRecursive(Prefab.transform.GetChild(i));
        }

        bool BuildArmatureRecursive(Transform transform)
        {
            int index = Joints.Count;
            bool enabled = ContainsBindPose(Skins, transform);
            Joints.Add(new Joint
            {
                Transform = transform,
            });

            for (int i = 0; i < transform.childCount; i++)
            {
                enabled |= BuildArmatureRecursive(transform.GetChild(i));
            }

            Joints[index].Enabled = enabled;

            return enabled;
        }

        static string GetBonePath(Transform root, Transform leaf)
        {
            var builder = new System.Text.StringBuilder();
            builder.Insert(0, leaf.name);
            while (leaf.parent != null && leaf.parent != root)
            {
                leaf = leaf.parent;
                builder.Insert(0, '/');
                builder.Insert(0, leaf.name);
            }
            return builder.ToString();
        }

        static int FindBone(List<Transform> Bones, Transform transform)
        {
            for (int i = 0; i < Bones.Count; i++)
            {
                if (Bones[i] == transform)
                {
                    return i;
                }
            }
            return -1;
        }

        static int FindBone(Transform[] Bones, Transform transform)
        {
            for (int i = 0; i < Bones.Length; i++)
            {
                if (Bones[i] == transform)
                {
                    return i;
                }
            }
            return -1;
        }

        static bool ContainsBindPose(List<Skin> Renderers, Transform transform)
        {
            foreach (var renderer in Renderers)
            {
                var index = FindBindPoseIndex(renderer.SkinnedMeshRenderer, transform);
                if (index == -1)
                    continue;
                return true;
            }
            return false;
        }

        static int FindBindPoseIndex(SkinnedMeshRenderer renderer, Transform transform)
        {
            for (int i = 0; i < renderer.bones.Length; i++)
            {
                if (renderer.bones[i] == transform)
                {
                    return i;
                }
            }
            return -1;
        }

        static bool TryFindBindPoseIndex(List<Transform> Bones, Transform transform, out int bindPoseIndex)
        {
            for (int i = 0; i < Bones.Count; i++)
            {
                if (Bones[i] == transform)
                {
                    bindPoseIndex = i;
                    return true;
                }
            }
            bindPoseIndex = -1;
            return false;
        }
    }
}
