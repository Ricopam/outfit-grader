using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader.Tests
{
    public enum NameStyle { Plain, VRoid }

    /// <summary>테스트용 T 자세 Humanoid 아바타를 코드로 만든다. 외부 모델 없이 매칭·배치·진단을 검증하기 위함.</summary>
    public sealed class TestAvatar
    {
        public GameObject Root;
        public Animator Animator;
        public Dictionary<HumanBodyBones, Transform> Bones = new Dictionary<HumanBodyBones, Transform>();
        public Transform this[HumanBodyBones bone] => Bones[bone];
    }

    public static class TestRigBuilder
    {
        // 바라보는 방향 +Z, 오른쪽 +X, 키 약 1.7 기준 위치
        static readonly (HumanBodyBones bone, HumanBodyBones parent, Vector3 position)[] Layout =
        {
            (HumanBodyBones.Hips, HumanBodyBones.LastBone, new Vector3(0f, 1.0f, 0f)),
            (HumanBodyBones.Spine, HumanBodyBones.Hips, new Vector3(0f, 1.1f, 0f)),
            (HumanBodyBones.Chest, HumanBodyBones.Spine, new Vector3(0f, 1.3f, 0f)),
            (HumanBodyBones.Neck, HumanBodyBones.Chest, new Vector3(0f, 1.5f, 0f)),
            (HumanBodyBones.Head, HumanBodyBones.Neck, new Vector3(0f, 1.6f, 0f)),
            (HumanBodyBones.LeftUpperLeg, HumanBodyBones.Hips, new Vector3(-0.1f, 0.95f, 0f)),
            (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg, new Vector3(-0.1f, 0.5f, 0f)),
            (HumanBodyBones.LeftFoot, HumanBodyBones.LeftLowerLeg, new Vector3(-0.1f, 0.1f, 0f)),
            (HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips, new Vector3(0.1f, 0.95f, 0f)),
            (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg, new Vector3(0.1f, 0.5f, 0f)),
            (HumanBodyBones.RightFoot, HumanBodyBones.RightLowerLeg, new Vector3(0.1f, 0.1f, 0f)),
            (HumanBodyBones.LeftUpperArm, HumanBodyBones.Chest, new Vector3(-0.2f, 1.45f, 0f)),
            (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm, new Vector3(-0.45f, 1.45f, 0f)),
            (HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm, new Vector3(-0.7f, 1.45f, 0f)),
            (HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftHand, new Vector3(-0.78f, 1.45f, 0.02f)),
            (HumanBodyBones.RightUpperArm, HumanBodyBones.Chest, new Vector3(0.2f, 1.45f, 0f)),
            (HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm, new Vector3(0.45f, 1.45f, 0f)),
            (HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm, new Vector3(0.7f, 1.45f, 0f)),
            (HumanBodyBones.RightIndexProximal, HumanBodyBones.RightHand, new Vector3(0.78f, 1.45f, 0.02f)),
        };

        public static string BoneName(HumanBodyBones bone, NameStyle style)
        {
            string plain = bone.ToString();
            if (style == NameStyle.Plain) return plain;
            if (plain.StartsWith("Left")) return "J_Bip_L_" + plain.Substring(4);
            if (plain.StartsWith("Right")) return "J_Bip_R_" + plain.Substring(5);
            return "J_Bip_C_" + plain;
        }

        /// <param name="scale">전체 크기 배율</param>
        /// <param name="headWidth">몸 메시에서 머리 정점의 좌우 폭(배율 적용 전)</param>
        /// <param name="withFingers">false면 검지 본을 만들지 않는다(손가락 없는 아바타 재현)</param>
        /// <param name="unitScale">FBX 단위 차이 재현용. Armature와 Body 오브젝트에 이 배율을 주고 내부 값은 반대로 줄인다(inuinu는 100).</param>
        /// <param name="headForward">머리 메시 상자를 앞(+Z)으로 미는 거리. 주둥이가 튀어나온 동물형 머리 재현용.</param>
        public static TestAvatar CreateHumanoid(string name, NameStyle style, float scale = 1f, float headWidth = 0.2f, bool withFingers = true,
            float unitScale = 1f, float headForward = 0f, float armRadius = 0f)
        {
            var avatar = new TestAvatar { Root = new GameObject(name) };
            Transform armature = new GameObject("Armature").transform;
            armature.SetParent(avatar.Root.transform, false);
            armature.localScale = Vector3.one * unitScale;

            foreach (var (bone, parent, position) in Layout)
            {
                bool isFinger = bone == HumanBodyBones.LeftIndexProximal || bone == HumanBodyBones.RightIndexProximal;
                if (isFinger && !withFingers) continue;

                Transform t = new GameObject(BoneName(bone, style)).transform;
                t.SetParent(parent == HumanBodyBones.LastBone ? armature : avatar.Bones[parent], false);
                t.position = position * scale;
                avatar.Bones[bone] = t;
            }

            AddBodyMesh(avatar, scale, headWidth, unitScale, headForward, armRadius);
            avatar.Animator = avatar.Root.AddComponent<Animator>();
            avatar.Animator.avatar = BuildAvatar(avatar);
            return avatar;
        }

        /// <param name="armRadius">0보다 크면 양쪽 위팔에 이 반지름(배율 적용 전)의 원통 단면 정점을 둔다</param>
        static void AddBodyMesh(TestAvatar avatar, float scale, float headWidth, float unitScale, float headForward, float armRadius)
        {
            float half = headWidth * 0.5f * scale;
            float hand = 0.08f * scale;
            var points = new List<(Vector3 pos, HumanBodyBones bone)>();
            // 머리: 목 위에 놓인 정육면체 (폭 = headWidth * scale)
            Vector3 headBase = avatar[HumanBodyBones.Head].position + Vector3.forward * headForward;
            foreach (float x in new[] { -half, half })
            foreach (float y in new[] { 0f, half * 2f })
            foreach (float z in new[] { -half, half })
                points.Add((headBase + new Vector3(x, y, z), HumanBodyBones.Head));
            points.AddRange(new List<(Vector3 pos, HumanBodyBones bone)>
            {
                (avatar[HumanBodyBones.Hips].position, HumanBodyBones.Hips),
                (avatar[HumanBodyBones.LeftHand].position, HumanBodyBones.LeftHand),
                (avatar[HumanBodyBones.LeftHand].position + Vector3.left * hand, HumanBodyBones.LeftHand),
                (avatar[HumanBodyBones.RightHand].position, HumanBodyBones.RightHand),
                (avatar[HumanBodyBones.RightHand].position + Vector3.right * hand, HumanBodyBones.RightHand),
            });
            if (armRadius > 0f)
            {
                AddArmRing(avatar, points, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, armRadius * scale);
                AddArmRing(avatar, points, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, armRadius * scale);
            }
            // 머리 정육면체 꼭짓점만 바깥 법선(중심 -> 꼭짓점)을 준다. 나머지는 법선 없음(관통 판정 표면에서 빠짐).
            Vector3 headCenter = headBase + Vector3.up * half;
            List<Vector3> normals = points.Select(p => p.bone == HumanBodyBones.Head ? (p.pos - headCenter).normalized : Vector3.zero).ToList();
            AddSkinnedMesh(avatar.Root.transform, "Body", points.Select(p => (p.pos, avatar[p.bone])).ToList(), unitScale, normals);
        }

        /// <summary>위팔 축(T 자세라 x축) 둘레에 반지름 radius인 원 두 개(8점씩)를 둔다.</summary>
        static void AddArmRing(TestAvatar avatar, List<(Vector3 pos, HumanBodyBones bone)> points, HumanBodyBones bone, HumanBodyBones end, float radius)
        {
            Vector3 a = avatar[bone].position, b = avatar[end].position;
            foreach (float t in new[] { 0.3f, 0.7f })
            {
                Vector3 center = Vector3.Lerp(a, b, t);
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4f;
                    points.Add((center + new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle)) * radius, bone));
                }
            }
        }

        /// <summary>정점마다 한 본에 100% 묶인 SkinnedMeshRenderer를 만든다.</summary>
        /// <param name="normals">월드 방향 법선(선택). 주면 메시 공간으로 바꿔 넣는다.</param>
        public static SkinnedMeshRenderer AddSkinnedMesh(Transform parent, string name, List<(Vector3 position, Transform bone)> points,
            float objectScale = 1f, IReadOnlyList<Vector3> normals = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * objectScale;
            var renderer = go.AddComponent<SkinnedMeshRenderer>();

            Transform[] bones = points.Select(p => p.bone).Distinct().ToArray();
            var mesh = new Mesh
            {
                vertices = points.Select(p => go.transform.InverseTransformPoint(p.position)).ToArray(),
                boneWeights = points.Select(p => new BoneWeight { boneIndex0 = System.Array.IndexOf(bones, p.bone), weight0 = 1f }).ToArray(),
                bindposes = bones.Select(b => b.worldToLocalMatrix * go.transform.localToWorldMatrix).ToArray(),
            };
            mesh.SetIndices(Enumerable.Range(0, points.Count).ToArray(), MeshTopology.Points, 0);
            if (normals != null) mesh.normals = normals.Select(n => go.transform.InverseTransformDirection(n)).ToArray();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = bones[0];
            return renderer;
        }

        static Avatar BuildAvatar(TestAvatar avatar)
        {
            var description = new HumanDescription
            {
                human = avatar.Bones.Select(pair => new HumanBone
                {
                    boneName = pair.Value.name,
                    humanName = HumanTrait.BoneName[(int)pair.Key],
                    limit = new HumanLimit { useDefaultValues = true },
                }).ToArray(),
                skeleton = avatar.Root.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                }).ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f,
            };
            Avatar built = AvatarBuilder.BuildHumanAvatar(avatar.Root, description);
            built.name = avatar.Root.name + "Avatar";
            return built;
        }

        /// <summary>원본 아바타 아래에 같은 이름의 본 사슬과 안경 본을 가진 의상을 만든다.</summary>
        public static GameObject CreateGlassesOutfit(TestAvatar source, NameStyle style, Vector3 glassesOffset)
        {
            var outfit = new GameObject("Glasses");
            outfit.transform.SetParent(source.Root.transform, false);
            Transform armature = new GameObject("Armature").transform;
            armature.SetParent(outfit.transform, false);

            Transform parent = armature;
            foreach (HumanBodyBones bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head })
            {
                Transform t = new GameObject(BoneName(bone, style)).transform;
                t.SetParent(parent, false);
                t.position = source[bone].position;
                parent = t;
            }

            Transform glassesBone = new GameObject("Glasses_Bone").transform;
            glassesBone.SetParent(parent, false);
            glassesBone.position = parent.position + glassesOffset;

            AddSkinnedMesh(outfit.transform, "GlassesMesh", new List<(Vector3, Transform)>
            {
                (glassesBone.position + Vector3.left * 0.05f, glassesBone),
                (glassesBone.position + Vector3.right * 0.05f, glassesBone),
            });
            return outfit;
        }

        public static void Destroy(params GameObject[] objects)
        {
            foreach (GameObject go in objects)
            {
                if (go == null) continue;
                var animator = go.GetComponent<Animator>();
                if (animator != null && animator.avatar != null) Object.DestroyImmediate(animator.avatar);
                foreach (var renderer in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.sharedMesh != null) Object.DestroyImmediate(renderer.sharedMesh);
                }
                Object.DestroyImmediate(go);
            }
        }
    }
}
