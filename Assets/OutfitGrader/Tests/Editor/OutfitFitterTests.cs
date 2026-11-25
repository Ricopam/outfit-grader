using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace OutfitGrader.Tests
{
    /// <summary>분석부터 맞추기, Undo까지 실제 흐름을 코드로 만든 아바타로 검증한다.</summary>
    public class OutfitFitterTests
    {
        static readonly Vector3 GlassesOffset = new Vector3(0f, 0.05f, 0.1f);

        TestAvatar source, chibi;
        GameObject outfit;

        [SetUp]
        public void SetUp()
        {
            source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain, scale: 1f, headWidth: 0.2f);
            chibi = TestRigBuilder.CreateHumanoid("Chibi", NameStyle.VRoid, scale: 0.5f, headWidth: 0.8f, withFingers: false);
            chibi.Root.transform.position = new Vector3(3f, 0f, 0f);
            outfit = TestRigBuilder.CreateGlassesOutfit(source, NameStyle.Plain, GlassesOffset);
        }

        [TearDown]
        public void TearDown()
        {
            TestRigBuilder.Destroy(source.Root, chibi.Root);
        }

        FitAnalysis Analyze()
        {
            return FitAnalysis.Run(new FitInput { Source = source.Animator, Outfit = outfit.transform, Target = chibi.Animator });
        }

        [Test]
        public void 이름_규칙에_맞는_귀_꼬리_털이_없으면_건너뛰었다고_보고한다()
        {
            // GA4: 코드로 만든 아바타에는 귀·꼬리·털 이름 본이 없다. 구멍·털 숨김을 하지 않고, 그 사실을 보고에 남긴다.
            FitResult result = OutfitFitter.Fit(Analyze(), new FitOptions { FixPenetration = true });

            string report = result.ToReport();
            StringAssert.Contains("부위 찾기: 귀 없음(건너뜀), 꼬리 없음(건너뜀), 털 없음(건너뜀)", report);
            StringAssert.DoesNotContain("구멍:", report);
            StringAssert.DoesNotContain("털 숨김:", report);
        }

        [Test]
        public void 꼬리_이름_본이_있으면_부위_찾기에_꼬리_있음으로_보고한다()
        {
            // Hips 아래 꼬리 본에 묶인 작은 꼬리 메시
            Transform hips = chibi[HumanBodyBones.Hips];
            var tail = new GameObject("Tail_01").transform;
            tail.SetParent(hips, false);
            tail.localPosition = new Vector3(0f, 0f, -0.1f);
            SkinnedMeshRenderer tailMesh = TestRigBuilder.AddSkinnedMesh(chibi.Root.transform, "TailMesh", new List<(Vector3, Transform)>
            {
                (tail.position, tail), (tail.position + new Vector3(0.02f, 0f, -0.05f), tail), (tail.position + new Vector3(-0.02f, 0.01f, -0.05f), tail),
            });
            tailMesh.sharedMesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);

            FitResult result = OutfitFitter.Fit(Analyze(), new FitOptions { FixPenetration = true });

            StringAssert.Contains("꼬리 있음", result.ToReport());
        }

        [Test]
        public void 분석은_실행_가능하고_안경_본은_부모를_따라간다()
        {
            FitAnalysis analysis = Analyze();

            Assert.IsTrue(analysis.CanFit, string.Join("\n", analysis.Diagnostics.Select(d => d.Problem)));
            Assert.AreEqual(5, analysis.Matches.Count(m => m.Kind == MatchKind.Humanoid));
            Assert.AreEqual(MatchKind.Unmatched, analysis.Matches.Single(m => m.OutfitBone.name == "Glasses_Bone").Kind);
            Assert.AreEqual(2f, analysis.Scale.Head, 1e-3f);
        }

        [Test]
        public void 안경이_대상_머리에_붙고_머리_비율만큼_떨어진다()
        {
            Vector3 originalGlasses = outfit.GetComponentsInChildren<Transform>().First(t => t.name == "Glasses_Bone").position;

            FitAnalysis analysis = Analyze();
            FitResult result = OutfitFitter.Fit(analysis);

            Transform fittedRoot = result.Avatar.transform;
            Transform fittedHead = fittedRoot.GetComponentsInChildren<Transform>().First(t => t.name == "J_Bip_C_Head");
            Transform fittedGlasses = fittedRoot.GetComponentsInChildren<Transform>().First(t => t.name == "Glasses_Bone");

            Assert.IsTrue(fittedGlasses.IsChildOf(fittedHead), "안경 본이 대상 머리 본 아래로 들어가야 한다");
            Vector3 expected = fittedHead.position + GlassesOffset * 2f;
            Assert.Less(Vector3.Distance(expected, fittedGlasses.position), 1e-3f, $"expected {expected} but {fittedGlasses.position}");
            Assert.AreEqual(analysis.Matches.Count(m => m.IsConnected), result.MovedBones);

            Vector3 originalAfter = outfit.GetComponentsInChildren<Transform>().First(t => t.name == "Glasses_Bone").position;
            Assert.AreEqual(originalGlasses, originalAfter, "원본 의상은 그대로여야 한다");
            Assert.IsTrue(fittedGlasses.IsChildOf(chibi[HumanBodyBones.Head]), "대상 아바타 자신의 머리 본에 입혀져야 한다");
        }

        [Test]
        public void 의상_복제본이_대상_아바타_안에_들어가고_원본_의상은_그대로다()
        {
            int sourceChildren = outfit.GetComponentsInChildren<Transform>(true).Length;

            FitResult result = OutfitFitter.Fit(Analyze());

            Assert.AreSame(chibi.Root, result.Avatar);
            Assert.AreSame(chibi.Root.transform, result.OutfitRoot.transform.parent, "의상은 대상 아바타 최상위 바로 아래에 들어가야 한다");
            Assert.AreSame(source.Root.transform, outfit.transform.parent);
            Assert.AreEqual(sourceChildren, outfit.GetComponentsInChildren<Transform>(true).Length, "원본 의상 구조는 그대로여야 한다");
        }

        [Test]
        public void 같은_의상을_다시_입히려_하면_겹침을_경고한다()
        {
            OutfitFitter.Fit(Analyze());

            FitAnalysis again = Analyze();

            Assert.IsTrue(again.CanFit, "경고일 뿐 실행은 막지 않는다");
            Assert.IsTrue(again.Diagnostics.Any(d => d.Severity == Severity.Warning && d.Problem.Contains("이미 입혀져")));
        }

        /// <summary>BOOTH 안경처럼 Armature 없이 MeshRenderer 하나로 된 소품. 피벗은 원점, 메시는 머리 높이.</summary>
        GameObject CreateRigidGlasses(Transform parent)
        {
            var prop = new GameObject("glasses_prop");
            prop.transform.SetParent(parent, false);
            prop.transform.position = source.Root.transform.position;
            Vector3 center = source[HumanBodyBones.Head].position + GlassesOffset;
            var mesh = new Mesh { vertices = new[] { center + Vector3.left * 0.05f, center + Vector3.right * 0.05f, center + Vector3.up * 0.01f } };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            prop.AddComponent<MeshFilter>().sharedMesh = mesh;
            prop.AddComponent<MeshRenderer>();
            return prop;
        }

        [Test]
        public void 본_없는_소품은_가장_가까운_머리에_붙고_머리_비율로_옮겨진다()
        {
            Object.DestroyImmediate(outfit);
            outfit = CreateRigidGlasses(source.Root.transform);
            Vector3 meshCenterOffset = outfit.GetComponent<MeshRenderer>().bounds.center - source[HumanBodyBones.Head].position;

            FitAnalysis analysis = Analyze();
            BoneMatch prop = analysis.Matches.Single();
            Assert.AreEqual(MatchKind.Attachment, prop.Kind);
            Assert.AreSame(chibi[HumanBodyBones.Head], prop.TargetBone);
            Assert.IsTrue(analysis.CanFit);

            FitResult result = OutfitFitter.Fit(analysis);

            Transform fittedHead = result.Avatar.GetComponentsInChildren<Transform>().First(t => t.name == "J_Bip_C_Head");
            Transform fittedProp = result.Avatar.GetComponentsInChildren<MeshRenderer>().Single().transform;
            Assert.AreSame(fittedHead, fittedProp.parent);
            Vector3 fittedCenter = fittedProp.TransformPoint(fittedProp.GetComponent<MeshFilter>().sharedMesh.bounds.center);
            Vector3 expected = fittedHead.position + meshCenterOffset * 2f;
            Assert.Less(Vector3.Distance(expected, fittedCenter), 1e-3f, $"expected {expected} but {fittedCenter}");
        }

        [Test]
        public void 머리_모양이_앞으로_튀어나온_대상에서도_소품은_머리_앞면에_붙는다()
        {
            const float muzzle = 0.3f;
            TestRigBuilder.Destroy(chibi.Root);
            chibi = TestRigBuilder.CreateHumanoid("Chibi", NameStyle.VRoid, scale: 0.5f, headWidth: 0.8f, withFingers: false, headForward: muzzle);
            chibi.Root.transform.position = new Vector3(3f, 0f, 0f);
            Object.DestroyImmediate(outfit);
            outfit = CreateRigidGlasses(source.Root.transform);
            Vector3 meshCenterOffset = outfit.GetComponent<MeshRenderer>().bounds.center - source[HumanBodyBones.Head].position;

            FitResult result = OutfitFitter.Fit(Analyze());

            Transform fittedHead = result.Avatar.GetComponentsInChildren<Transform>().First(t => t.name == "J_Bip_C_Head");
            Transform fittedProp = result.Avatar.GetComponentsInChildren<MeshRenderer>().Single().transform;
            Vector3 fittedCenter = fittedProp.TransformPoint(fittedProp.GetComponent<MeshFilter>().sharedMesh.bounds.center);
            Vector3 expected = fittedHead.position + meshCenterOffset * 2f + Vector3.forward * muzzle;
            Assert.Less(Vector3.Distance(expected, fittedCenter), 1e-3f, $"expected {expected} but {fittedCenter}");
            StringAssert.Contains("구역 상대 위치", result.ToReport());
        }

        [Test]
        public void 소매는_대상_팔_굵기만큼_두꺼워지고_길이는_팔_길이_비율을_따른다()
        {
            TestRigBuilder.Destroy(source.Root, chibi.Root);
            source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain, armRadius: 0.04f);
            chibi = TestRigBuilder.CreateHumanoid("Chibi", NameStyle.VRoid, scale: 0.5f, armRadius: 0.16f);
            outfit = new GameObject("Sleeve");
            outfit.transform.SetParent(source.Root.transform, false);
            Transform sleeveBone = new GameObject("LeftUpperArm").transform;
            sleeveBone.SetParent(outfit.transform, false);
            sleeveBone.position = source[HumanBodyBones.LeftUpperArm].position;
            Vector3 mid = Vector3.Lerp(source[HumanBodyBones.LeftUpperArm].position, source[HumanBodyBones.LeftLowerArm].position, 0.5f);
            TestRigBuilder.AddSkinnedMesh(outfit.transform, "SleeveMesh", new List<(Vector3, Transform)>
            {
                (mid + Vector3.up * 0.05f, sleeveBone), (mid + Vector3.down * 0.05f, sleeveBone),
            });

            FitResult result = OutfitFitter.Fit(Analyze());

            Transform fittedSleeve = chibi[HumanBodyBones.LeftUpperArm].GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("LeftUpperArm_"));
            Vector3 lossy = fittedSleeve.lossyScale;
            Assert.AreEqual(0.5f, lossy.x, 1e-3f, "팔 방향(x)은 길이 비율");
            Assert.AreEqual(2f, lossy.y, 1e-3f, "단면 방향은 굵기 비율");
            Assert.AreEqual(2f, lossy.z, 1e-3f);
            StringAssert.Contains("굵기 x2.00", result.ToReport());
        }

        [Test]
        public void 소맷부리가_매달린_손_본은_몸_비율이_아니라_아래팔_굵기만큼_균등하게_커진다()
        {
            TestRigBuilder.Destroy(source.Root, chibi.Root);
            source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain, armRadius: 0.04f);
            chibi = TestRigBuilder.CreateHumanoid("Chibi", NameStyle.VRoid, scale: 0.5f, armRadius: 0.16f);
            // 테스트 아바타는 위팔에만 몸 고리가 있어 아래팔 굵기를 재도록 아래팔 고리를 더한다(원본 0.04, 치비 0.08 -> 굵기 x2).
            AddForearmRing(source, 0.04f);
            AddForearmRing(chibi, 0.08f);
            outfit = new GameObject("Sleeve");
            outfit.transform.SetParent(source.Root.transform, false);
            Transform forearm = new GameObject("LeftLowerArm").transform;
            forearm.SetParent(outfit.transform, false);
            forearm.position = source[HumanBodyBones.LeftLowerArm].position;
            Transform hand = new GameObject("LeftHand").transform;
            hand.SetParent(forearm, false);
            hand.position = source[HumanBodyBones.LeftHand].position;
            TestRigBuilder.AddSkinnedMesh(outfit.transform, "SleeveMesh", new List<(Vector3, Transform)>
            {
                (forearm.position + Vector3.up * 0.05f, forearm), (hand.position + Vector3.up * 0.05f, hand),
            });

            FitResult result = OutfitFitter.Fit(Analyze());

            Transform fittedHand = chibi.Root.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("LeftHand_"));
            Vector3 lossy = fittedHand.lossyScale;
            Assert.AreEqual(2f, lossy.x, 1e-3f, "아래팔 굵기 배율(x2)로, 몸 비율(x0.5)이 아니다");
            Assert.AreEqual(lossy.x, lossy.y, 1e-3f, "손은 길이 방향이 없어 균등 배율");
            Assert.AreEqual(lossy.y, lossy.z, 1e-3f);
            StringAssert.Contains("균등 x2.00, 아래팔 굵기", result.ToReport());
        }

        [Test]
        public void 손가락까지_덮는_장갑의_손_본은_소맷부리_규칙을_쓰지_않는다()
        {
            TestRigBuilder.Destroy(source.Root, chibi.Root);
            source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain, armRadius: 0.04f);
            chibi = TestRigBuilder.CreateHumanoid("Chibi", NameStyle.VRoid, scale: 0.5f, armRadius: 0.16f);
            AddForearmRing(source, 0.04f);
            AddForearmRing(chibi, 0.08f);
            outfit = new GameObject("Glove");
            outfit.transform.SetParent(source.Root.transform, false);
            Transform hand = new GameObject("LeftHand").transform;
            hand.SetParent(outfit.transform, false);
            hand.position = source[HumanBodyBones.LeftHand].position;
            Transform finger = new GameObject("LeftIndexProximal").transform;
            finger.SetParent(hand, false);
            finger.position = source[HumanBodyBones.LeftIndexProximal].position;
            TestRigBuilder.AddSkinnedMesh(outfit.transform, "GloveMesh", new List<(Vector3, Transform)>
            {
                (hand.position + Vector3.up * 0.02f, hand), (finger.position + Vector3.up * 0.02f, finger),
            });

            OutfitFitter.Fit(Analyze());

            Transform fittedHand = chibi.Root.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("LeftHand_"));
            Assert.AreEqual(0.5f, fittedHand.lossyScale.x, 1e-3f, "손가락 본이 있으면 손바닥만 커지지 않게 몸 비율 그대로");
        }

        /// <summary>왼쪽 아래팔 축(LowerArm -> Hand)을 두르는 원형 고리 정점을 아래팔 본에 묶은 몸 메시</summary>
        static void AddForearmRing(TestAvatar avatar, float radius)
        {
            Transform lower = avatar[HumanBodyBones.LeftLowerArm];
            Vector3 a = lower.position, b = avatar[HumanBodyBones.LeftHand].position;
            var points = new List<(Vector3, Transform)>();
            for (float t = 0.1f; t < 0.95f; t += 0.1f)
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f;
                points.Add((Vector3.Lerp(a, b, t) + new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle)) * radius, lower));
            }
            TestRigBuilder.AddSkinnedMesh(avatar.Root.transform, "Forearm", points);
        }

        [Test]
        public void 굵기를_맞춘_본_아래_따라가는_자식은_균등_배율_보조_오브젝트로_옮긴다()
        {
            TestRigBuilder.Destroy(source.Root, chibi.Root);
            source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain, armRadius: 0.04f);
            chibi = TestRigBuilder.CreateHumanoid("Chibi", NameStyle.VRoid, scale: 0.5f, armRadius: 0.16f);
            outfit = new GameObject("Sleeve");
            outfit.transform.SetParent(source.Root.transform, false);
            Transform sleeveBone = new GameObject("LeftUpperArm").transform;
            sleeveBone.SetParent(outfit.transform, false);
            sleeveBone.position = source[HumanBodyBones.LeftUpperArm].position;
            Transform ribbon = new GameObject("Ribbon").transform;
            ribbon.SetParent(sleeveBone, false);
            ribbon.localPosition = new Vector3(0.1f, 0.05f, 0f);
            TestRigBuilder.AddSkinnedMesh(outfit.transform, "SleeveMesh", new List<(Vector3, Transform)>
            {
                (sleeveBone.position + Vector3.up * 0.05f, sleeveBone), (ribbon.position, ribbon),
            });
            int before = chibi.Root.GetComponentsInChildren<Transform>(true).Length;

            OutfitFitter.Fit(Analyze());

            Transform fittedRibbon = chibi.Root.GetComponentsInChildren<Transform>().First(t => t.name == "Ribbon");
            StringAssert.EndsWith("_자식", fittedRibbon.parent.name);
            Vector3 lossy = fittedRibbon.lossyScale;
            Assert.AreEqual(lossy.x, lossy.y, 1e-3f, "자식은 축마다 같은 배율이어야 비틀리지 않는다");
            Assert.AreEqual(lossy.y, lossy.z, 1e-3f);

            Undo.PerformUndo();
            Assert.AreEqual(before, chibi.Root.GetComponentsInChildren<Transform>(true).Length, "보조 오브젝트도 Undo로 사라져야 한다");
        }

        [Test]
        public void 같은_의상이나_다른_의상의_자식은_입력에서_한_번만_남긴다()
        {
            Transform glassesBone = outfit.GetComponentsInChildren<Transform>().First(t => t.name == "Glasses_Bone");
            var other = new GameObject("Other").transform;

            List<Transform> result = OutfitInputList.WithoutOverlaps(new[] { outfit.transform, outfit.transform, glassesBone, other, null });

            CollectionAssert.AreEqual(new[] { outfit.transform, other, null }, result);
            Object.DestroyImmediate(other.gameObject);
        }

        [Test]
        public void 관통_밀어내기는_몸_안_정점을_표면_밖으로_옮긴_새_메시를_저장한다()
        {
            // 맞춘 뒤 대상(치비) 머리 정육면체 꼭짓점 1cm 안쪽에 오도록 원본 쪽 정점을 잡는다(머리 비율 2).
            Transform outfitHead = outfit.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
            Vector3 cornerFromHead = new Vector3(0.2f, 0.4f, 0.2f);
            Vector3 normal = new Vector3(0.2f, 0.2f, 0.2f).normalized;
            Vector3 sourceVertex = outfitHead.position + (cornerFromHead - normal * 0.01f) / 2f;
            // 메시 오브젝트를 본 아래에 둔다: 맞추면 본과 함께 의상 복제본 밖(대상 머리 본 아래)으로 옮겨진다.
            SkinnedMeshRenderer cap = TestRigBuilder.AddSkinnedMesh(outfitHead, "Cap", new List<(Vector3, Transform)> { (sourceVertex, outfitHead) });
            Mesh originalMesh = cap.sharedMesh;

            FitResult result = OutfitFitter.Fit(Analyze(), new FitOptions { FixPenetration = true });

            var fittedCap = (SkinnedMeshRenderer)result.Meshes.Single(r => r.name == "Cap");
            Assert.IsFalse(fittedCap.transform.IsChildOf(result.OutfitRoot.transform), "본과 함께 복제본 밖으로 옮겨진 메시도 보정 대상이어야 한다");
            Assert.AreNotSame(originalMesh, fittedCap.sharedMesh);
            Assert.IsFalse(EditorUtility.IsPersistent(fittedCap.sharedMesh), "맞추기 시점에는 에셋을 만들지 않는다(Undo 뒤 고아 에셋 방지)");
            Vector3 corner = chibi[HumanBodyBones.Head].position + cornerFromHead;
            Assert.Greater(Vector3.Dot(SkinnedPosition(fittedCap, 0) - corner, normal), 0f, "몸 표면 바깥(여유 간격 쪽)으로 나와야 한다");
            StringAssert.Contains("관통 보정: Cap 정점 1개", result.ToReport());
            Assert.AreSame(originalMesh, cap.sharedMesh, "원본 의상 메시는 그대로");

            string prefab = ResultPrefabSaver.Save(result.Avatar, "Chibi_Cap", out _);
            try
            {
                StringAssert.StartsWith(ResultPrefabSaver.MeshFolder, AssetDatabase.GetAssetPath(fittedCap.sharedMesh), "프리팹 저장 때 메시 에셋으로 저장");
            }
            finally
            {
                DeleteSavedOutput(prefab);
            }
        }

        /// <summary>
        /// 테스트가 저장한 프리팹과 그 프리팹이 참조하는 출력 폴더의 메시만 지우고, 폴더는 비었을 때만 지운다.
        /// 사용자가 출력 폴더에 저장해 둔 결과를 테스트가 지우면 안 된다.
        /// </summary>
        static void DeleteSavedOutput(string prefabPath)
        {
            if (prefabPath == null) return;
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            string[] meshPaths = saved == null ? new string[0] : saved.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh)
                .Concat(saved.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh))
                .Select(AssetDatabase.GetAssetPath)
                .Where(p => p.StartsWith(ResultPrefabSaver.MeshFolder)).Distinct().ToArray();
            AssetDatabase.DeleteAsset(prefabPath);
            foreach (string mesh in meshPaths) AssetDatabase.DeleteAsset(mesh);
            foreach (string folder in new[] { ResultPrefabSaver.MeshFolder, ResultPrefabSaver.OutputFolder })
            {
                bool isEmpty = AssetDatabase.IsValidFolder(folder) && AssetDatabase.FindAssets(string.Empty, new[] { folder }).Length == 0;
                if (isEmpty) AssetDatabase.DeleteAsset(folder);
            }
        }

        static Vector3 SkinnedPosition(SkinnedMeshRenderer renderer, int index)
        {
            Mesh mesh = renderer.sharedMesh;
            int bone = mesh.boneWeights[index].boneIndex0;
            return (renderer.bones[bone].localToWorldMatrix * mesh.bindposes[bone]).MultiplyPoint3x4(mesh.vertices[index]);
        }

        [Test]
        public void 여러_의상을_한_번에_입히고_Undo_한_번에_모두_되돌린다()
        {
            GameObject prop = CreateRigidGlasses(source.Root.transform);
            prop.name = "Hat";
            int before = chibi.Root.GetComponentsInChildren<Transform>(true).Length;
            var analyses = new[] { outfit, prop }
                .Select(o => FitAnalysis.Run(new FitInput { Source = source.Animator, Outfit = o.transform, Target = chibi.Animator }))
                .ToList();

            List<FitResult> results = OutfitFitter.FitAll(analyses);

            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results.All(r => r.OutfitRoot != null && r.OutfitRoot.transform.IsChildOf(chibi.Root.transform)));

            Undo.PerformUndo();

            Assert.IsTrue(results.All(r => r.OutfitRoot == null), "두 의상 모두 사라져야 한다");
            Assert.AreEqual(before, chibi.Root.GetComponentsInChildren<Transform>(true).Length);
        }

        [Test]
        public void 정점이_쓰지_않는_빈_본_칸은_깨진_의상으로_보지_않는다()
        {
            // Modular Avatar 방식 의상: 본 목록에 쓰지 않는 빈 칸이 섞여 있다.
            SkinnedMeshRenderer mesh = outfit.GetComponentInChildren<SkinnedMeshRenderer>();
            mesh.bones = mesh.bones.Concat(new Transform[] { null, null }).ToArray();
            mesh.sharedMesh.bindposes = mesh.sharedMesh.bindposes.Concat(new[] { Matrix4x4.identity, Matrix4x4.identity }).ToArray();

            Assert.IsFalse(FitAnalysis.IsBroken(mesh));
            Assert.IsTrue(Analyze().CanFit);
        }

        [Test]
        public void 정점이_쓰는_본이_비어_있으면_깨진_의상으로_막는다()
        {
            SkinnedMeshRenderer mesh = outfit.GetComponentInChildren<SkinnedMeshRenderer>();
            Transform[] bones = mesh.bones;
            bones[0] = null;
            mesh.bones = bones;

            Assert.IsTrue(FitAnalysis.IsBroken(mesh));
            Assert.IsFalse(Analyze().CanFit);
        }

        [Test]
        public void 본_없는_소품을_다시_입히려_해도_겹침을_경고한다()
        {
            // 의상 최상위가 곧 소품이면 복제본째 대상 머리 본 아래로 들어간다.
            Object.DestroyImmediate(outfit);
            outfit = CreateRigidGlasses(source.Root.transform);
            FitResult first = OutfitFitter.Fit(Analyze());
            Assert.AreSame(chibi[HumanBodyBones.Head], first.OutfitRoot.transform.parent);

            FitAnalysis again = Analyze();

            Assert.IsTrue(again.Diagnostics.Any(d => d.Severity == Severity.Warning && d.Problem.Contains("이미 입혀져")));
        }

        [Test]
        public void 정리용_빈_오브젝트_아래의_소품도_머리에_붙는다()
        {
            Object.DestroyImmediate(outfit);
            outfit = new GameObject("Outfit");
            outfit.transform.SetParent(source.Root.transform, false);
            var group = new GameObject("Accessories").transform;
            group.SetParent(outfit.transform, false);
            CreateRigidGlasses(group);

            BoneMatch prop = Analyze().Matches.Single(m => m.Kind == MatchKind.Attachment);

            Assert.AreSame(chibi[HumanBodyBones.Head], prop.TargetBone);
        }

        [Test]
        public void 비활성_소품도_메시_위치로_부착할_본을_고른다()
        {
            Object.DestroyImmediate(outfit);
            outfit = CreateRigidGlasses(source.Root.transform);
            outfit.SetActive(false);

            Assert.AreSame(chibi[HumanBodyBones.Head], Analyze().Matches.Single().TargetBone);
        }

        [Test]
        public void 이름이_같은_형제_소품도_각자_옮겨진다()
        {
            Object.DestroyImmediate(outfit);
            outfit = new GameObject("Earrings");
            outfit.transform.SetParent(source.Root.transform, false);
            GameObject left = CreateRigidGlasses(outfit.transform);
            GameObject right = CreateRigidGlasses(outfit.transform);
            left.name = right.name = "Earring";
            left.transform.position += Vector3.left * 0.08f;
            right.transform.position += Vector3.right * 0.08f;

            FitResult result = OutfitFitter.Fit(Analyze());

            Transform[] fitted = result.Avatar.GetComponentsInChildren<MeshRenderer>().Select(r => r.transform).ToArray();
            Assert.AreEqual(2, fitted.Length);
            Assert.IsTrue(fitted.All(t => t.parent.name == "J_Bip_C_Head"), "두 귀걸이 모두 대상 머리에 붙어야 한다");
            Assert.Greater(Vector3.Distance(fitted[0].position, fitted[1].position), 0.1f, "좌우 귀걸이가 한 위치로 겹치면 안 된다");
        }

        [Test]
        public void 원본_본_아래에_직접_놓은_소품은_그_본에_붙는다()
        {
            Object.DestroyImmediate(outfit);
            outfit = CreateRigidGlasses(source[HumanBodyBones.Chest]);

            BoneMatch prop = Analyze().Matches.Single();

            Assert.AreSame(chibi[HumanBodyBones.Chest], prop.TargetBone, "가장 가까운 본(Head)이 아니라 놓여 있던 본을 따라야 한다");
        }

        [Test]
        public void 결과를_프리팹으로_저장하면_안경_본까지_들어간다()
        {
            FitResult result = OutfitFitter.Fit(Analyze());
            string path = ResultPrefabSaver.Save(result.Avatar, "Chibi_Glasses", out _);
            try
            {
                Assert.IsNotNull(path);
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(saved);
                Assert.IsTrue(saved.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Glasses_Bone"));
            }
            finally
            {
                DeleteSavedOutput(path);
            }
        }

        [Test]
        public void Undo_한_번으로_대상_아바타가_원래대로_돌아온다()
        {
            int before = chibi.Root.GetComponentsInChildren<Transform>(true).Length;
            int headChildren = chibi[HumanBodyBones.Head].childCount;
            FitResult result = OutfitFitter.Fit(Analyze());
            Assert.Greater(chibi.Root.GetComponentsInChildren<Transform>(true).Length, before);

            Undo.PerformUndo();

            Assert.IsTrue(result.OutfitRoot == null, "Undo 후 의상 복제본이 삭제되어야 한다");
            Assert.AreEqual(before, chibi.Root.GetComponentsInChildren<Transform>(true).Length, "대상 본 아래로 옮긴 의상 본도 남지 않아야 한다");
            Assert.AreEqual(headChildren, chibi[HumanBodyBones.Head].childCount);
        }

        [Test]
        public void 본_없는_스킨_메시_모자는_깨진_의상이_아니라_머리에_붙는_소품이다()
        {
            // BOOTH 니트 모자처럼 본 없이 블렌드쉐이프만 가진 SkinnedMeshRenderer
            Object.DestroyImmediate(outfit);
            outfit = new GameObject("knit_cap");
            outfit.transform.SetParent(source[HumanBodyBones.Head], false);
            Vector3 top = source[HumanBodyBones.Head].position + Vector3.up * 0.15f;
            var mesh = new Mesh { vertices = new[] { top + Vector3.left * 0.1f, top + Vector3.right * 0.1f, top + Vector3.up * 0.05f } };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.vertices = mesh.vertices.Select(v => outfit.transform.InverseTransformPoint(v)).ToArray();
            mesh.RecalculateBounds();
            var cap = outfit.AddComponent<SkinnedMeshRenderer>();
            cap.sharedMesh = mesh;

            FitAnalysis analysis = Analyze();

            Assert.IsFalse(FitAnalysis.IsBroken(cap));
            Assert.IsTrue(analysis.CanFit, string.Join("\n", analysis.Diagnostics.Select(d => d.Problem)));
            BoneMatch prop = analysis.Matches.Single();
            Assert.AreEqual(MatchKind.Attachment, prop.Kind);
            Assert.AreSame(chibi[HumanBodyBones.Head], prop.TargetBone);

            FitResult result = OutfitFitter.Fit(analysis, new FitOptions { FixPenetration = true });
            Transform fittedHead = result.Avatar.GetComponentsInChildren<Transform>().First(t => t.name == "J_Bip_C_Head");
            Assert.AreSame(fittedHead, result.Meshes.Single().transform.parent, "관통 보정을 켜도 대상 머리 본에 붙는다");
        }

        [Test]
        public void 같이_옮기는_후드와_모자는_같은_머리_비율을_쓴다()
        {
            // 후드: 원본 머리 본과 같은 이름의 본 사슬(Modular Avatar 방식)에 묶여 머리 상자를 넓힌다.
            Object.DestroyImmediate(outfit);
            var hood = new GameObject("Hood").transform;
            hood.SetParent(source.Root.transform, false);
            Transform parent = hood;
            foreach (HumanBodyBones bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head })
            {
                Transform copy = new GameObject(source[bone].name).transform;
                copy.SetParent(parent, false);
                copy.position = source[bone].position;
                parent = copy;
            }
            Vector3 head = source[HumanBodyBones.Head].position;
            TestRigBuilder.AddSkinnedMesh(hood, "HoodMesh", new List<(Vector3, Transform)>
            {
                (head + Vector3.left * 0.2f, parent), (head + Vector3.right * 0.2f, parent),
            });
            Transform hat = CreateRigidGlasses(source[HumanBodyBones.Head]).transform;
            var outfits = new List<Transform> { hood, hat };

            float[] heads = outfits.Select(o => FitAnalysis.Run(new FitInput
            {
                Source = source.Animator, Outfit = o, Target = chibi.Animator, OtherOutfits = outfits.Where(x => x != o).ToList(),
            }).Scale.Head).ToArray();

            Assert.AreEqual(heads[0], heads[1], 1e-4f, "후드 위에 쓴 모자가 후드 안으로 들어가지 않으려면 비율이 같아야 한다");
        }

        [Test]
        public void 정수리를_덮지_않는_안경은_관통_보정을_켜도_메시를_바꾸지_않는다()
        {
            // 안경다리가 머리카락에 닿아도 휘지 않게, 두상 밀어내기는 정수리를 덮는 소품(모자)에만 한다.
            Object.DestroyImmediate(outfit);
            outfit = CreateRigidGlasses(source.Root.transform);
            Mesh original = outfit.GetComponent<MeshFilter>().sharedMesh;

            FitResult result = OutfitFitter.Fit(Analyze(), new FitOptions { FixPenetration = true });

            Assert.AreSame(original, result.Avatar.GetComponentsInChildren<MeshFilter>().Single().sharedMesh);
        }
    }
}
