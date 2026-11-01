using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds an in-place walk cycle on the Knight metarig, plus an Animator Controller and a prefab.
/// The FBX already contains the Blender metarig and skin weights; this only authors the clip.
/// Run it again from Tools/Build Knight Walk.
/// </summary>
public static class KnightWalkBuilder
{
    const string ModelPath = "Assets/Fantasy_Heroes_Free/FBX/Characters/Knight.fbx";
    const string ClipPath = "Assets/Fantasy_Heroes_Free/Animations/Knight_Walk.anim";
    const string ControllerPath = "Assets/Fantasy_Heroes_Free/Animations/Knight.controller";
    const string PrefabPath = "Assets/Fantasy_Heroes_Free/Prefabs/Knight.prefab";
    const string LogPath = "Temp/BuildKnightWalk.log";

    const int Samples = 16;
    const float ThighAmplitude = 28f;
    const float KneeBend = 48f;
    const float KneeBase = 10f;
    const float ArmAmplitude = 24f;
    const float ElbowBend = 32f;
    const float BodyLean = 7f;
    const float HipYaw = 5f;

    [MenuItem("Tools/Build Knight Walk")]
    public static void Build()
    {
        var log = new StringBuilder();
        try
        {
            log.AppendLine("Unity " + Application.unityVersion);
            var avatar = ConfigureImporter(log);
            EnsureFolder("Assets/Fantasy_Heroes_Free", "Animations");
            EnsureFolder("Assets/Fantasy_Heroes_Free", "Prefabs");

            var clip = CreateWalkClip(log);
            if (File.Exists(ClipPath))
                AssetDatabase.DeleteAsset(ClipPath);
            AssetDatabase.CreateAsset(clip, ClipPath);

            if (File.Exists(ControllerPath))
                AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            var state = machine.AddState("Walk");
            state.motion = clip;
            machine.defaultState = state;

            AssetDatabase.SaveAssets();
            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            state = controller.layers[0].stateMachine.defaultState;
            state.motion = clip;

            CreatePrefab(controller, avatar, log);
            WireSceneKnight(controller, avatar, log);

            AssetDatabase.SaveAssets();
            log.AppendLine("SUCCESS");
            log.AppendLine("clip=" + ClipPath);
            log.AppendLine("controller=" + ControllerPath);
            log.AppendLine("prefab=" + PrefabPath);
        }
        catch (Exception e)
        {
            log.AppendLine("FAILED");
            log.AppendLine(e.ToString());
            Debug.LogError(e);
        }

        File.WriteAllText(LogPath, log.ToString());
        Debug.Log(log.ToString());
    }

    static Avatar ConfigureImporter(StringBuilder log)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        if (importer == null)
            throw new InvalidOperationException("Model importer not found for " + ModelPath);

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.optimizeGameObjects = false;
        importer.SaveAndReimport();

        Avatar avatar = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            if (asset is Avatar candidate)
                avatar = candidate;
        }

        log.AppendLine(avatar == null
            ? "avatar=missing"
            : "avatar=" + avatar.name + " valid=" + avatar.isValid + " human=" + avatar.isHuman);
        return avatar;
    }

    static AnimationClip CreateWalkClip(StringBuilder log)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
            throw new InvalidOperationException("Could not load " + ModelPath);

        var root = UnityEngine.Object.Instantiate(model);
        root.name = "Knight";
        root.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            LogRig(root, log);
            var rig = new Rig(root.transform);
            float stride = rig.MeasureStride();
            float duration = Mathf.Clamp(stride / 1.55f, 0.7f, 1.05f);
            log.AppendLine("stride=" + stride.ToString("0.000") + " duration=" + duration.ToString("0.000"));
            log.AppendLine(rig.DescribeRest());
            log.AppendLine(rig.DescribePose(0.25f));
            log.AppendLine(rig.DescribePose(0.75f));
            log.AppendLine(rig.DescribeFootRange());

            var times = new float[Samples + 1];
            var bones = new List<Transform>();
            CollectAnimated(rig, bones);

            var rotations = new Dictionary<Transform, Quaternion[]>();
            var positions = new Dictionary<Transform, Vector3[]>();
            foreach (var bone in bones)
            {
                rotations[bone] = new Quaternion[Samples + 1];
                positions[bone] = new Vector3[Samples + 1];
            }

            for (int i = 0; i <= Samples; i++)
            {
                float phase = (i % Samples) / (float)Samples;
                times[i] = duration * i / Samples;
                rig.Pose(phase);
                foreach (var bone in bones)
                {
                    rotations[bone][i] = bone.localRotation;
                    positions[bone][i] = bone.localPosition;
                }
            }

            var clip = new AnimationClip { frameRate = 30f, name = "Knight_Walk" };
            foreach (var bone in bones)
            {
                string path = RelativePath(bone, root.transform);
                WriteQuaternion(clip, path, times, rotations[bone]);
                if (bone == rig.Spine)
                    WriteVector(clip, path, "localPosition", times, positions[bone]);
            }

            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            settings.loopBlend = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void CollectAnimated(Rig rig, List<Transform> bones)
    {
        bones.Add(rig.Spine);
        bones.Add(rig.SpineMid);
        bones.Add(rig.Chest);
        if (rig.Head != null)
            bones.Add(rig.Head);
        bones.Add(rig.ThighL);
        bones.Add(rig.ThighR);
        bones.Add(rig.ShinL);
        bones.Add(rig.ShinR);
        bones.Add(rig.FootL);
        bones.Add(rig.FootR);
        bones.Add(rig.UpperArmL);
        bones.Add(rig.UpperArmR);
        bones.Add(rig.ForearmL);
        bones.Add(rig.ForearmR);
    }

    static void CreatePrefab(AnimatorController controller, Avatar avatar, StringBuilder log)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var instance = UnityEngine.Object.Instantiate(model);
        instance.name = "Knight";
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var animator = instance.GetComponent<Animator>();
            if (animator == null)
                animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            instance.hideFlags = HideFlags.None;

            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            log.AppendLine("prefab saved");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    static void WireSceneKnight(AnimatorController controller, Avatar avatar, StringBuilder log)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            log.AppendLine("scene skipped: editor is in play mode");
            return;
        }

        GameObject knight = null;
        foreach (var agent in UnityEngine.Object.FindObjectsByType<UnityEngine.AI.NavMeshAgent>(FindObjectsSortMode.None))
        {
            if (agent.gameObject.name == "Knight")
            {
                knight = agent.gameObject;
                break;
            }
        }

        if (knight == null)
        {
            log.AppendLine("scene knight not found");
            return;
        }

        var scene = knight.scene;
        bool wasDirty = scene.isDirty;
        var animator = knight.GetComponent<Animator>();
        if (animator == null)
            animator = Undo.AddComponent<Animator>(knight);
        Undo.RecordObject(animator, "Assign Knight Walk");
        animator.runtimeAnimatorController = controller;
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        EditorUtility.SetDirty(animator);
        EditorSceneManager.MarkSceneDirty(scene);

        if (!wasDirty)
        {
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("scene knight wired and saved");
        }
        else
        {
            log.AppendLine("scene knight wired; scene was already dirty so it was not saved");
        }
    }

    static void LogRig(GameObject root, StringBuilder log)
    {
        log.AppendLine("root=" + root.name);
        var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        log.AppendLine("skinnedMeshes=" + skins.Length);
        foreach (var skin in skins)
        {
            log.AppendLine("  mesh=" + skin.name
                + " bones=" + (skin.bones == null ? 0 : skin.bones.Length)
                + " rootBone=" + (skin.rootBone == null ? "null" : skin.rootBone.name));
        }

        if (skins.Length == 0)
            throw new InvalidOperationException("Knight imported without skinned meshes. The walk would not deform the model.");
    }

    static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, name);
    }

    static string RelativePath(Transform transform, Transform root)
    {
        var names = new List<string>();
        while (transform != null && transform != root)
        {
            names.Add(transform.name);
            transform = transform.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    static void WriteQuaternion(AnimationClip clip, string path, float[] times, Quaternion[] values)
    {
        WriteCurve(clip, path, "localRotation.x", times, Sample(values, v => v.x));
        WriteCurve(clip, path, "localRotation.y", times, Sample(values, v => v.y));
        WriteCurve(clip, path, "localRotation.z", times, Sample(values, v => v.z));
        WriteCurve(clip, path, "localRotation.w", times, Sample(values, v => v.w));
    }

    static void WriteVector(AnimationClip clip, string path, string property, float[] times, Vector3[] values)
    {
        WriteCurve(clip, path, property + ".x", times, Sample(values, v => v.x));
        WriteCurve(clip, path, property + ".y", times, Sample(values, v => v.y));
        WriteCurve(clip, path, property + ".z", times, Sample(values, v => v.z));
    }

    static float[] Sample<T>(T[] values, Func<T, float> select)
    {
        var result = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
            result[i] = select(values[i]);
        return result;
    }

    static void WriteCurve(AnimationClip clip, string path, string property, float[] times, float[] values)
    {
        var curve = new AnimationCurve();
        for (int i = 0; i < times.Length; i++)
            curve.AddKey(new Keyframe(times[i], values[i]));

        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        }

        if (curve.length >= 3)
        {
            int last = curve.length - 1;
            float dt = (curve[1].time - curve[0].time) * 2f;
            float tangent = dt > 0.0001f ? (curve[1].value - curve[last - 1].value) / dt : 0f;
            var start = curve[0];
            var end = curve[last];
            start.inTangent = start.outTangent = tangent;
            end.inTangent = end.outTangent = tangent;
            curve.MoveKey(0, start);
            curve.MoveKey(last, end);
        }

        clip.SetCurve(path, typeof(Transform), property, curve);
    }

    sealed class Rig
    {
        public readonly Transform Root;
        public readonly Transform Spine;
        public readonly Transform SpineMid;
        public readonly Transform Chest;
        public readonly Transform Head;
        public readonly Transform ThighL;
        public readonly Transform ThighR;
        public readonly Transform ShinL;
        public readonly Transform ShinR;
        public readonly Transform FootL;
        public readonly Transform FootR;
        public readonly Transform ToeL;
        public readonly Transform ToeR;
        public readonly Transform UpperArmL;
        public readonly Transform UpperArmR;
        public readonly Transform ForearmL;
        public readonly Transform ForearmR;
        public readonly Transform HandL;
        public readonly Transform HandR;

        readonly Dictionary<Transform, Quaternion> restLocalRot = new Dictionary<Transform, Quaternion>();
        readonly Dictionary<Transform, Vector3> restLocalPos = new Dictionary<Transform, Vector3>();
        readonly Dictionary<Transform, Quaternion> restWorldRot = new Dictionary<Transform, Quaternion>();
        readonly Vector3 forward;
        readonly Vector3 right;
        readonly Vector3 kneeAxisL;
        readonly Vector3 kneeAxisR;
        readonly Vector3 elbowAxisL;
        readonly Vector3 elbowAxisR;
        readonly Vector3 footAxisL;
        readonly Vector3 footAxisR;
        readonly Vector3 leanAxis;
        readonly int armSignL;
        readonly int armSignR;
        readonly float plantedFootY;

        public Rig(Transform root)
        {
            Root = root;
            Spine = Find(root, "spine");
            SpineMid = Find(root, "spine.001");
            Chest = Find(root, "spine.003");
            Head = Find(root, "spine.006");
            ThighL = Find(root, "thigh.L");
            ThighR = Find(root, "thigh.R");
            ShinL = Find(root, "shin.L");
            ShinR = Find(root, "shin.R");
            FootL = Find(root, "foot.L");
            FootR = Find(root, "foot.R");
            ToeL = Find(root, "toe.L");
            ToeR = Find(root, "toe.R");
            UpperArmL = Find(root, "upper_arm.L");
            UpperArmR = Find(root, "upper_arm.R");
            ForearmL = Find(root, "forearm.L");
            ForearmR = Find(root, "forearm.R");
            HandL = Find(root, "hand.L");
            HandR = Find(root, "hand.R");

            Require(Spine, "spine");
            Require(SpineMid, "spine.001");
            Require(Chest, "spine.003");
            Require(ThighL, "thigh.L");
            Require(ThighR, "thigh.R");
            Require(ShinL, "shin.L");
            Require(ShinR, "shin.R");
            Require(FootL, "foot.L");
            Require(FootR, "foot.R");
            Require(ToeL, "toe.L");
            Require(ToeR, "toe.R");
            Require(UpperArmL, "upper_arm.L");
            Require(UpperArmR, "upper_arm.R");
            Require(ForearmL, "forearm.L");
            Require(ForearmR, "forearm.R");
            Require(HandL, "hand.L");
            Require(HandR, "hand.R");

            Snapshot(root);

            Vector3 across = ThighR.position - ThighL.position;
            across.y = 0f;
            Vector3 toeForward = ((ToeL.position - FootL.position) + (ToeR.position - FootR.position));
            toeForward.y = 0f;
            forward = toeForward.sqrMagnitude > 0.0001f ? toeForward.normalized : Vector3.forward;
            right = Vector3.Cross(Vector3.up, forward);
            if (Vector3.Dot(right, across) < 0f)
                right = -right;

            kneeAxisL = PickHinge(ShinL, FootL, delta => delta.y * 0.5f - Vector3.Dot(delta, forward));
            kneeAxisR = PickHinge(ShinR, FootR, delta => delta.y * 0.5f - Vector3.Dot(delta, forward));

            Vector3 chestPoint = Chest.position + forward * 0.2f;
            Vector3 handL0 = HandL.position;
            Vector3 handR0 = HandR.position;
            elbowAxisL = PickHinge(ForearmL, HandL, delta =>
                (handL0 - chestPoint).magnitude - (handL0 + delta - chestPoint).magnitude);
            elbowAxisR = PickHinge(ForearmR, HandR, delta =>
                (handR0 - chestPoint).magnitude - (handR0 + delta - chestPoint).magnitude);

            footAxisL = AxisAround(FootL, right);
            footAxisR = AxisAround(FootR, right);
            leanAxis = PickHinge(SpineMid, Head != null ? Head : Chest, delta =>
                Vector3.Dot(delta, forward) - Mathf.Abs(Vector3.Dot(delta, right)));

            armSignL = ArmSwingSign(UpperArmL, ForearmL, HandL, -right);
            armSignR = ArmSwingSign(UpperArmR, ForearmR, HandR, right);
            ResetPose();
            plantedFootY = Mathf.Min(FootL.position.y, FootR.position.y);
        }

        public void Pose(float phase)
        {
            ResetPose();
            float w = phase * Mathf.PI * 2f;
            float sin = Mathf.Sin(w);
            float lift = Mathf.Max(0f, Mathf.Cos(w));
            float liftOpposite = Mathf.Max(0f, -Mathf.Cos(w));

            Vector3 upLocal = Spine.parent.InverseTransformDirection(Vector3.up).normalized;
            Vector3 rightLocal = Spine.parent.InverseTransformDirection(right).normalized;
            Spine.localPosition = restLocalPos[Spine] + rightLocal * (sin * 0.012f);

            var hipTurn = Quaternion.AngleAxis(sin * HipYaw, Vector3.up);
            ApplyWorldDelta(Spine, hipTurn);
            SpineMid.localRotation = restLocalRot[SpineMid] * Quaternion.AngleAxis(BodyLean, leanAxis);
            ApplyWorldDelta(Chest, Quaternion.Inverse(hipTurn) * Quaternion.AngleAxis(-sin * HipYaw, Vector3.up));
            if (Head != null)
                ApplyWorldDelta(Head, Quaternion.Inverse(Quaternion.AngleAxis(-sin * HipYaw, Vector3.up)));

            // Positive swing currently takes the foot backward, so the left leg uses -sin
            // to step forward on the first half of the cycle, opposite the right arm.
            PoseLeg(ThighL, ShinL, FootL, ToeL, kneeAxisL, footAxisL, -sin, lift);
            PoseLeg(ThighR, ShinR, FootR, ToeR, kneeAxisR, footAxisR, sin, liftOpposite);
            PlantLowerFoot(upLocal);
            PoseArm(UpperArmL, ForearmL, -right, elbowAxisL, -sin * ArmAmplitude * armSignL);
            PoseArm(UpperArmR, ForearmR, right, elbowAxisR, sin * ArmAmplitude * armSignR);
        }

        public float MeasureStride()
        {
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int i = 0; i < Samples; i++)
            {
                Pose(i / (float)Samples);
                float f = Vector3.Dot(FootL.position - Root.position, forward);
                min = Mathf.Min(min, f);
                max = Mathf.Max(max, f);
            }

            ResetPose();
            return max - min;
        }

        public string DescribeRest()
        {
            ResetPose();
            return "rest footL=" + Fmt(FootL) + " footR=" + Fmt(FootR) + " handL=" + Fmt(HandL) + " plantedY=" + plantedFootY.ToString("0.000");
        }

        public string DescribeFootRange()
        {
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < Samples; i++)
            {
                Pose(i / (float)Samples);
                minY = Mathf.Min(minY, FootL.position.y, FootR.position.y);
                maxY = Mathf.Max(maxY, FootL.position.y, FootR.position.y);
            }

            ResetPose();
            return "footY min=" + minY.ToString("0.000") + " max=" + maxY.ToString("0.000") + " plant=" + plantedFootY.ToString("0.000");
        }

        public string DescribePose(float phase)
        {
            Pose(phase);
            string text = "phase=" + phase.ToString("0.00")
                + " footL=" + Fmt(FootL)
                + " footR=" + Fmt(FootR)
                + " handL=" + Fmt(HandL)
                + " handR=" + Fmt(HandR)
                + " head=" + Fmt(Head != null ? Head : Chest);
            ResetPose();
            return text;
        }

        void PlantLowerFoot(Vector3 upLocal)
        {
            float correction = plantedFootY - Mathf.Min(FootL.position.y, FootR.position.y);
            Spine.localPosition += upLocal * correction;
        }

        void PoseLeg(Transform thigh, Transform shin, Transform foot, Transform toe, Vector3 kneeAxis, Vector3 footAxis, float swing, float lift)
        {
            ApplyWorldDelta(thigh, Quaternion.AngleAxis(swing * ThighAmplitude, right));
            shin.localRotation = restLocalRot[shin] * Quaternion.AngleAxis(KneeBase + lift * KneeBend, kneeAxis);
            LevelFoot(foot, toe, footAxis);
        }

        void PoseArm(Transform upper, Transform forearm, Vector3 outward, Vector3 elbowAxis, float swingDegrees)
        {
            Vector3 target = Quaternion.AngleAxis(swingDegrees, right) * (Vector3.down + forward * 0.22f + outward * 0.28f).normalized;
            PointBoneAt(upper, forearm, target);
            forearm.localRotation = restLocalRot[forearm] * Quaternion.AngleAxis(ElbowBend, elbowAxis);
        }

        void LevelFoot(Transform foot, Transform toe, Vector3 axis)
        {
            float bestAngle = 0f;
            float bestScore = float.PositiveInfinity;
            for (int angle = -50; angle <= 55; angle += 5)
            {
                foot.localRotation = restLocalRot[foot] * Quaternion.AngleAxis(angle, axis);
                Vector3 dir = (toe.position - foot.position).normalized;
                float score = dir.y * dir.y - 0.05f * Vector3.Dot(dir, forward);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestAngle = angle;
                }
            }

            foot.localRotation = restLocalRot[foot] * Quaternion.AngleAxis(bestAngle, axis);
        }

        string Fmt(Transform bone)
        {
            Vector3 p = bone.position - Root.position;
            float f = Vector3.Dot(p, forward);
            float r = Vector3.Dot(p, right);
            return "(" + f.ToString("0.00") + "," + p.y.ToString("0.00") + "," + r.ToString("0.00") + ")";
        }

        int ArmSwingSign(Transform upper, Transform forearm, Transform hand, Vector3 outward)
        {
            Vector3 origin = hand.position;
            float Score(float degrees)
            {
                ResetPose();
                Vector3 target = Quaternion.AngleAxis(degrees, right) * (Vector3.down + forward * 0.22f + outward * 0.28f).normalized;
                PointBoneAt(upper, forearm, target);
                return Vector3.Dot(hand.position - origin, forward);
            }

            int sign = Score(30f) >= Score(-30f) ? 1 : -1;
            ResetPose();
            return sign;
        }

        Vector3 PickHinge(Transform bone, Transform tip, Func<Vector3, float> score)
        {
            Vector3 tipStart = tip.position;
            Quaternion saved = bone.localRotation;
            Vector3 boneDir = ((bone.childCount > 0 ? bone.GetChild(0).position : tip.position) - bone.position).normalized;
            float best = float.NegativeInfinity;
            Vector3 bestAxis = bone.InverseTransformDirection(Vector3.right);

            foreach (var world in new[] { Vector3.up, forward, right })
            {
                Vector3 projected = Vector3.ProjectOnPlane(world, boneDir);
                if (projected.sqrMagnitude < 0.05f)
                    continue;

                Vector3 axisLocal = bone.InverseTransformDirection(projected.normalized).normalized;
                foreach (int sign in new[] { 1, -1 })
                {
                    bone.localRotation = saved * Quaternion.AngleAxis(35f * sign, axisLocal);
                    float value = score(tip.position - tipStart);
                    if (value > best)
                    {
                        best = value;
                        bestAxis = axisLocal * sign;
                    }

                    bone.localRotation = saved;
                }
            }

            return bestAxis;
        }

        static Vector3 AxisAround(Transform bone, Vector3 worldAxis)
        {
            Vector3 direction = bone.childCount > 0
                ? bone.GetChild(0).position - bone.position
                : bone.TransformDirection(Vector3.forward);
            Vector3 projected = Vector3.ProjectOnPlane(worldAxis, direction);
            if (projected.sqrMagnitude < 0.0001f)
                projected = Vector3.ProjectOnPlane(Vector3.up, direction);
            return bone.InverseTransformDirection(projected.normalized).normalized;
        }

        void PointBoneAt(Transform bone, Transform child, Vector3 worldDirection)
        {
            Vector3 current = child.position - bone.position;
            if (current.sqrMagnitude < 0.000001f || worldDirection.sqrMagnitude < 0.000001f)
                return;
            bone.rotation = Quaternion.FromToRotation(current, worldDirection) * bone.rotation;
        }

        void ApplyWorldDelta(Transform bone, Quaternion worldDelta)
        {
            Quaternion parentWorld = bone.parent.rotation;
            Quaternion parentDelta = parentWorld * Quaternion.Inverse(restWorldRot[bone.parent]);
            Quaternion desiredWorld = parentDelta * worldDelta * restWorldRot[bone];
            bone.localRotation = Quaternion.Inverse(parentWorld) * desiredWorld;
        }

        void ResetPose()
        {
            foreach (var pair in restLocalRot)
            {
                pair.Key.localRotation = pair.Value;
                pair.Key.localPosition = restLocalPos[pair.Key];
            }
        }

        void Snapshot(Transform transform)
        {
            restLocalRot[transform] = transform.localRotation;
            restLocalPos[transform] = transform.localPosition;
            restWorldRot[transform] = transform.rotation;
            for (int i = 0; i < transform.childCount; i++)
                Snapshot(transform.GetChild(i));
        }

        static void Require(Transform transform, string name)
        {
            if (transform == null)
                throw new InvalidOperationException("Missing bone '" + name + "'. The Knight rig was not imported.");
        }

        static Transform Find(Transform transform, string name)
        {
            if (transform.name == name)
                return transform;
            for (int i = 0; i < transform.childCount; i++)
            {
                var found = Find(transform.GetChild(i), name);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
