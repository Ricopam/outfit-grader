using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    /// <summary>표면 간격 보존 맞춤: 원통 반지름 표, 간격 대응, 부위 판정, 두 겹 옷 배치, 겹침 정리</summary>
    public class SurfaceOffsetTests
    {
        static IEnumerable<(float t, float angle, float radius)> Ring(float t, float radius, int count = 24)
        {
            for (int i = 0; i < count; i++) yield return (t, i * 2f * Mathf.PI / count, radius);
        }

        [Test]
        public void 고른_원통은_어느_칸에서나_같은_반지름이고_빈_줄은_이웃_줄로_채운다()
        {
            CylinderTable table = CylinderTable.Build(new[] { 0.4f, 0.5f, 0.6f }.SelectMany(t => Ring(t, 0.1f)));

            Assert.IsTrue(table.HasData);
            Assert.AreEqual(0.1f, table.Sample(0.5f, 1.3f), 1e-4f);
            Assert.AreEqual(0.1f, table.Sample(0f, 0f), 1e-4f, "정점이 없는 줄은 가까운 줄 값을 쓴다");
            Assert.IsFalse(CylinderTable.Build(new (float, float, float)[0]).HasData);
        }

        [Test]
        public void 정점이_몇_칸만_잡힌_줄의_빈_칸은_표면_거리가_가까운_위아래_줄로_채운다()
        {
            // 저폴리 배(inuinu): 가운데 줄은 뒤쪽 두 칸(옆구리 0.2)만 잡히고, 위아래 줄은 앞까지 0.1로 잡혔다.
            var samples = Ring(0.3f, 0.1f).Concat(Ring(0.7f, 0.1f))
                .Append((0.5f, Mathf.PI - 0.1f, 0.2f)).Append((0.5f, Mathf.PI + 0.1f, 0.2f));
            const float axisLength = 0.12f;

            CylinderTable table = CylinderTable.Build(samples, axisLength);

            Assert.AreEqual(0.1f, table.Sample(0.5f, 0f), 0.01f, "배 앞 빈 칸은 둘레 반대편 옆구리가 아니라 위아래 줄을 따른다");
            Assert.AreEqual(0.2f, table.Sample(0.53f, Mathf.PI), 0.01f, "잡힌 칸은 그대로");
            Assert.AreEqual(0.2f, CylinderTable.Build(samples).Sample(0.53f, 0f), 0.01f, "축 길이를 모르면 같은 줄로만 채운다");
        }

        [Test]
        public void 같은_줄로_채울_때는_표면_거리가_가장_가까운_칸을_쓴다()
        {
            // 둘레 2칸 떨어진 0.2 칸(호 0.105)과 3칸 떨어진 0.05 칸(호 0.039): 표면 거리로는 0.05 칸이 더 가깝다.
            float Angle(int bin) => (bin + 0.5f) * 2f * Mathf.PI / CylinderTable.AngleBins;
            var samples = new[] { (0.5f, Angle(2), 0.2f), (0.5f, Angle(CylinderTable.AngleBins - 3), 0.05f) };

            CylinderTable table = CylinderTable.Build(samples, 0f);

            // 움푹한 칸 메우기가 이웃 평균으로 조금 올리지만, 칸 차이로 골랐다면 0.2 그대로였다.
            Assert.Less(table.Sample(0.53f, Angle(0)), 0.15f);
        }

        [Test]
        public void 위아래_줄로_채울_때는_줄_차이만큼_보간한다()
        {
            // 한 각도 칸에서 아래 1줄은 0.1, 위 3줄은 0.3: 가운데 빈 칸은 1/4 지점인 0.15.
            float Angle(int bin) => (bin + 0.5f) * 2f * Mathf.PI / CylinderTable.AngleBins;
            float T(int row) => (row + 0.5f) / CylinderTable.LengthBins;
            var samples = Enumerable.Range(0, CylinderTable.AngleBins).SelectMany(j => new[] { (T(4), Angle(j), 0.1f), (T(8), Angle(j), 0.3f) })
                .Append((T(5), Angle(12), 0.3f)).ToList();

            CylinderTable table = CylinderTable.Build(samples, 1f);

            Assert.AreEqual(0.15f, table.Sample(T(5), Angle(0)), 0.01f, "줄 5 앞 칸: 줄 4(0.1)와 줄 8(0.3) 사이 1/4");
        }

        [Test]
        public void 길이_방향으로_한_줄만_꺼진_칸은_위아래_평균으로_메우고_여러_줄_오목함은_둔다()
        {
            // 한 각도 칸에서 줄마다 0.19, 0.16, 0.19가 번갈아 잡힌 성긴 옆·뒤 경계(inuinu 127도 칸)와, 여러 줄에 걸친 허리 오목함.
            float Angle(int bin) => (bin + 0.5f) * 2f * Mathf.PI / CylinderTable.AngleBins;
            float T(int row) => (row + 0.5f) / CylinderTable.LengthBins;
            float[] profile = { 0.19f, 0.16f, 0.19f, 0.16f, 0.19f, 0.15f, 0.12f, 0.10f, 0.10f, 0.12f, 0.15f, 0.18f, 0.18f, 0.18f, 0.18f, 0.18f };
            var samples = Enumerable.Range(0, CylinderTable.LengthBins).SelectMany(i => Enumerable.Range(0, CylinderTable.AngleBins).Select(j => (T(i), Angle(j), profile[i]))).ToList();

            CylinderTable table = CylinderTable.Build(samples, 1f);

            Assert.AreEqual(0.19f, table.Sample(T(1), Angle(0)), 0.005f, "한 줄만 꺼진 칸은 위아래 평균");
            Assert.Less(table.Sample(T(7), Angle(0)), 0.12f, "여러 줄에 걸친 오목함(허리)은 그대로");
        }

        [Test]
        public void 움푹_꺼진_칸은_이웃으로_메우고_볼록한_칸은_깎지_않는다()
        {
            // 둘레 24칸 중 한 칸은 정점이 안쪽에만 잡혀 0.05로 꺼지고, 다른 한 칸은 가슴처럼 0.15로 돌출됐다.
            float center = Mathf.PI / CylinderTable.AngleBins;
            var samples = Ring(0.5f, 0.1f).Where(p => Mathf.Abs(p.angle) > 1e-3f && Mathf.Abs(p.angle - Mathf.PI) > 1e-3f)
                .Append((0.5f, center, 0.05f)).Append((0.5f, Mathf.PI + center, 0.15f));
            CylinderTable table = CylinderTable.Build(samples);

            Assert.Greater(table.Sample(0.5f, center), 0.08f, "꺼진 칸은 이웃 쪽으로 메워져 옷이 그 칸만 안으로 끌리지 않는다");
            Assert.GreaterOrEqual(table.Sample(0.5f, Mathf.PI + center), 0.15f - 1e-3f, "돌출부는 깎지 않아 옷이 몸 안으로 들어가지 않는다");
        }

        [Test]
        public void 간격_대응은_층_순서를_지키고_몸_밖_옷은_여유_간격_이상_떨어진다()
        {
            const float sourceSurface = 0.1f, targetSurface = 0.2f, margin = 0.005f;
            float[] rhos = { 0.05f, 0.099f, 0.1f, 0.101f, 0.12f, 0.15f };

            float[] mapped = rhos.Select(r => CylinderTable.MapRadius(r, sourceSurface, targetSurface, margin)).ToArray();

            for (int i = 1; i < mapped.Length; i++) Assert.Greater(mapped[i], mapped[i - 1], "안쪽 층이 바깥 층을 넘지 않는다");
            Assert.AreEqual(targetSurface + margin, mapped[2], 1e-5f, "몸 표면에 닿은 옷은 대상 표면 + 여유 간격");
            Assert.AreEqual(targetSurface + margin + 0.02f * 2f, mapped[4], 1e-5f, "원본 간격 x 굵기 비율");
            Assert.AreEqual(mapped[2], CylinderTable.MapRadius(0.0999999f, sourceSurface, targetSurface, margin), 1e-4f, "표면 앞뒤로 끊기지 않는다");
        }

        [TestCase("Breast_L", "Chest", BodyRegion.Torso)]
        [TestCase("Tail.001", "Hips", BodyRegion.None)]
        [TestCase("FurChest", "Chest", BodyRegion.None)]
        [TestCase("Hair_01", "Head", BodyRegion.None)]
        [TestCase("Sleeve_L", "LeftLowerArm", BodyRegion.LeftLowerArm)]
        public void 몸_본_아래_흔들림_본은_부위에_넣고_꼬리_털_머리카락은_뺀다(string boneName, string parent, BodyRegion expected)
        {
            TestAvatar avatar = TestRigBuilder.CreateHumanoid("Rig", NameStyle.Plain);
            try
            {
                var bone = new GameObject(boneName).transform;
                bone.SetParent(avatar[(HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), parent)], false);

                Assert.AreEqual(expected, BodyCylinders.RegionOfBone(AvatarRig.FromAnimator(avatar.Animator), bone));
            }
            finally
            {
                TestRigBuilder.Destroy(avatar.Root);
            }
        }

        /// <summary>몸통 축(Hips 1.0 -> Neck 1.5)을 두르는 타원 고리 정점(좌우 반지름 rx, 앞뒤 rz)을 Chest 본에 묶어 몸통 단면을 만든다.</summary>
        static void AddTorso(TestAvatar avatar, float rx, float rz)
        {
            Transform chest = avatar[HumanBodyBones.Chest];
            Vector3 root = avatar.Root.transform.position;
            var points = new List<(Vector3, Transform)>();
            for (float y = 1.05f; y < 1.46f; y += 0.05f)
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f;
                points.Add((root + new Vector3(Mathf.Cos(a) * rx, y, Mathf.Sin(a) * rz), chest));
            }
            TestRigBuilder.AddSkinnedMesh(avatar.Root.transform, "Torso", points);
        }

        [Test]
        public void 다시_배치_여유_간격은_다리_길이가_아니라_몸통_굵기를_따른다()
        {
            // 치비처럼 다리가 짧아도(엉덩이 높이는 같음) 몸통이 두 배 굵으면 여유 간격도 두 배여야 한다.
            TestAvatar thin = TestRigBuilder.CreateHumanoid("Thin", NameStyle.Plain);
            TestAvatar thick = TestRigBuilder.CreateHumanoid("Thick", NameStyle.Plain);
            TestAvatar bare = TestRigBuilder.CreateHumanoid("Bare", NameStyle.Plain);
            try
            {
                AddTorso(thin, 0.1f, 0.1f);
                AddTorso(thick, 0.2f, 0.2f);
                BodyCylinders Body(TestAvatar a) => BodyCylinders.Build(BodyPointCloud.Collect(AvatarRig.FromAnimator(a.Animator), new Transform[0], includeBodyChildren: true));
                const float hipHeight = 1f;

                float thinMargin = SurfaceOffsetFitter.MarginOf(Body(thin), hipHeight);
                float thickMargin = SurfaceOffsetFitter.MarginOf(Body(thick), hipHeight);

                Assert.AreEqual(0.1f * SurfaceOffsetFitter.MarginToTorsoRadius, thinMargin, 1e-4f);
                Assert.AreEqual(thinMargin * 2f, thickMargin, 1e-4f, "엉덩이 높이가 같아도 몸통 굵기에 비례");
                Assert.AreEqual(hipHeight * PenetrationFixer.MarginToHipHeight, SurfaceOffsetFitter.MarginOf(Body(bare), hipHeight), 1e-6f,
                    "몸통 정점이 없으면 엉덩이 높이 기준");
            }
            finally
            {
                TestRigBuilder.Destroy(thin.Root, thick.Root, bare.Root);
            }
        }

        [Test]
        public void 관통_밀어내기_최대_거리는_다리_길이가_아니라_몸통_굵기를_따른다()
        {
            TestAvatar thin = TestRigBuilder.CreateHumanoid("Thin", NameStyle.Plain);
            TestAvatar thick = TestRigBuilder.CreateHumanoid("Thick", NameStyle.Plain);
            TestAvatar bare = TestRigBuilder.CreateHumanoid("Bare", NameStyle.Plain);
            try
            {
                AddTorso(thin, 0.1f, 0.1f);
                AddTorso(thick, 0.2f, 0.2f);
                BodyCylinders Body(TestAvatar a) => BodyCylinders.Build(BodyPointCloud.Collect(AvatarRig.FromAnimator(a.Animator), new Transform[0], includeBodyChildren: true));
                const float hipHeight = 1f;

                float thinPush = SurfaceOffsetFitter.MaxPushOf(Body(thin), hipHeight);

                Assert.AreEqual(0.1f * SurfaceOffsetFitter.MaxPushToTorsoRadius, thinPush, 1e-4f);
                Assert.AreEqual(thinPush * 2f, SurfaceOffsetFitter.MaxPushOf(Body(thick), hipHeight), 1e-4f, "엉덩이 높이가 같아도 몸통 굵기에 비례");
                Assert.AreEqual(hipHeight * PenetrationFixer.MaxPushToHipHeight, SurfaceOffsetFitter.MaxPushOf(Body(bare), hipHeight), 1e-6f,
                    "몸통 정점이 없으면 엉덩이 높이 기준");
                Assert.AreEqual(hipHeight * PenetrationFixer.MaxPushToHipHeight, SurfaceOffsetFitter.MaxPushOf(null, hipHeight), 1e-6f,
                    "표면 간격 보존을 끄면(원통 없음) 엉덩이 높이 기준 그대로");
            }
            finally
            {
                TestRigBuilder.Destroy(thin.Root, thick.Root, bare.Root);
            }
        }

        [Test]
        public void 겹침_정리_미는_거리는_다리_길이가_아니라_몸통_굵기를_따른다()
        {
            TestAvatar thin = TestRigBuilder.CreateHumanoid("Thin", NameStyle.Plain);
            TestAvatar thick = TestRigBuilder.CreateHumanoid("Thick", NameStyle.Plain);
            TestAvatar bare = TestRigBuilder.CreateHumanoid("Bare", NameStyle.Plain);
            try
            {
                AddTorso(thin, 0.1f, 0.1f);
                AddTorso(thick, 0.2f, 0.2f);
                BodyCylinders Body(TestAvatar a) => BodyCylinders.Build(BodyPointCloud.Collect(AvatarRig.FromAnimator(a.Animator), new Transform[0], includeBodyChildren: true));
                const float hipHeight = 1f;

                float thinStep = ClothPierceResolver.StepOf(Body(thin), hipHeight);

                Assert.AreEqual(0.1f * ClothPierceResolver.StepToTorsoRadius, thinStep, 1e-4f);
                Assert.AreEqual(thinStep * 2f, ClothPierceResolver.StepOf(Body(thick), hipHeight), 1e-4f, "엉덩이 높이가 같아도 몸통 굵기에 비례");
                Assert.AreEqual(hipHeight * ClothPierceResolver.StepToHipHeight, ClothPierceResolver.StepOf(Body(bare), hipHeight), 1e-6f,
                    "몸통 정점이 없으면 엉덩이 높이 기준");
            }
            finally
            {
                TestRigBuilder.Destroy(thin.Root, thick.Root, bare.Root);
            }
        }

        [Test]
        public void 뚫린_자리만_밀어도_이웃이_완만하게_따라와_가시가_생기지_않는다()
        {
            // 5 x 5 평면 격자(법선 위, 정점 번호 = z * 5 + x). 삼각형 (12, 17, 13) 하나만 뚫렸다.
            var vertices = new List<Vector3>();
            for (int z = 0; z < 5; z++) for (int x = 0; x < 5; x++) vertices.Add(new Vector3(x, 0f, z));
            var triangles = new List<int>();
            for (int z = 0; z < 4; z++)
            for (int x = 0; x < 4; x++)
            {
                int i = z * 5 + x;
                triangles.AddRange(new[] { i, i + 5, i + 1, i + 1, i + 5, i + 6 });
            }
            int[] tri = triangles.ToArray();
            var field = new PushField(vertices.ToArray(), tri, Enumerable.Repeat(Vector3.up, vertices.Count).ToArray());
            var isPierced = new bool[tri.Length / 3];
            isPierced[Enumerable.Range(0, isPierced.Length).First(t => tri[t * 3] == 12 && tri[t * 3 + 1] == 17)] = true;

            field.Push(isPierced, 1f);
            float Height(int v) => field.WorldDelta(v).y;

            Assert.AreEqual(1f, Height(12), 1e-5f, "뚫린 정점은 한 번 미는 거리만큼");
            Assert.Greater(Height(7), 0f, "뚫리지 않은 바로 옆(한 고리) 정점도 따라 올라온다");
            Assert.Greater(Height(2), 0f, "두 고리 떨어진 정점까지 이어진다");
            Assert.Less(Height(7), Height(12), "뚫린 정점보다 낮다");
            Assert.Less(Height(2), Height(7), "멀어질수록 낮아진다(가시 대신 언덕)");
        }

        [Test]
        public void 양면_옷의_겹친_앞뒷면_정점도_함께_밀린다()
        {
            // 같은 위치에 법선이 반대인 앞면(0~2)·뒷면(3~5). 법선을 그냥 더하면 0이 되어 밀리지 않던 경우.
            var p = new[] { Vector3.zero, Vector3.right, Vector3.forward };
            Vector3[] vertices = p.Concat(p).ToArray();
            int[] tri = { 0, 2, 1, 3, 4, 5 };
            Vector3[] normals = Enumerable.Repeat(Vector3.up, 3).Concat(Enumerable.Repeat(Vector3.down, 3)).ToArray();
            var field = new PushField(vertices, tri, normals);

            field.Push(new[] { true, false }, 0.5f);

            Assert.AreEqual(0.5f, field.WorldDelta(0).magnitude, 1e-5f, "앞면 정점이 밀린다");
            Assert.AreEqual(field.WorldDelta(0), field.WorldDelta(3), "같은 위치의 뒷면 정점도 같은 방향·거리로 함께 밀린다");
        }

        [Test]
        public void 짧고_굵은_몸통에서_오버핏은_목을_고정한_채_아랫단을_길게_두고_정사이즈는_몸_길이에_맞춘다()
        {
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain);
            TestAvatar target = TestRigBuilder.CreateHumanoid("Target", NameStyle.Plain);
            try
            {
                AddTorso(source, 0.1f, 0.1f);
                AddTorso(target, 0.15f, 0.15f);
                // 대상 몸통 길이(Hips 1.0 -> Neck)를 0.5에서 0.2로 줄여 길이 x0.4, 굵기 x1.5(눌림 3.75배)로 만든다.
                Transform neck = target[HumanBodyBones.Neck];
                neck.position = new Vector3(neck.position.x, 1.2f, neck.position.z);
                BodyCylinders Body(TestAvatar a) => BodyCylinders.Build(BodyPointCloud.Collect(AvatarRig.FromAnimator(a.Animator), new Transform[0], includeBodyChildren: true));
                BodyCylinders from = Body(source), to = Body(target);

                const FitStyle over = FitStyle.Oversized, regular = FitStyle.Regular;
                Assert.IsTrue(BodyCylinders.TryMap(from, to, BodyRegion.Torso, new Vector3(0.1f, 1.5f, 0f), 0.005f, over, out Vector3 atNeck));
                Assert.IsTrue(BodyCylinders.TryMap(from, to, BodyRegion.Torso, new Vector3(0.1f, 1.0f, 0f), 0.005f, over, out Vector3 atHem));
                Assert.IsTrue(BodyCylinders.TryMap(from, to, BodyRegion.Torso, new Vector3(0.1f, 1.0f, 0f), 0.005f, regular, out Vector3 regularHem));

                Assert.AreEqual(1.2f, atNeck.y, 1e-3f, "목 높이 옷은 대상 목 높이에 고정");
                // 오버핏(눌림 2배까지): 길이 배율 = max(0.4, 1.5 / 2) = 0.75 -> 원본 몸통 0.5만큼 아래의 아랫단은 목에서 0.375 아래.
                // 아랫단 한도(허벅지 절반)가 그보다 커서 막히지 않는다.
                Assert.AreEqual(1.2f - 0.5f * 0.75f, atHem.y, 0.01f, "오버핏 아랫단은 몸 길이(0.2)보다 길게 내려온다");
                // 정사이즈(눌림 4배까지): 눌림 1.5 / 0.4 = 3.75배라 상한 안 -> 몸 길이 그대로
                Assert.AreEqual(1.0f, regularHem.y, 1e-3f, "정사이즈 아랫단은 대상 엉덩이 높이");
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, target.Root);
            }
        }

        [Test]
        public void 몸통이_충분히_길면_아랫단을_늘이지_않는다()
        {
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain);
            TestAvatar target = TestRigBuilder.CreateHumanoid("Target", NameStyle.Plain);
            try
            {
                // 길이 같고(x1) 굵기 x1.5: 눌림 1.5배로 상한(2배) 안이라 늘이지 않는다.
                AddTorso(source, 0.1f, 0.1f);
                AddTorso(target, 0.15f, 0.15f);
                BodyCylinders Body(TestAvatar a) => BodyCylinders.Build(BodyPointCloud.Collect(AvatarRig.FromAnimator(a.Animator), new Transform[0], includeBodyChildren: true));

                Assert.IsTrue(BodyCylinders.TryMap(Body(source), Body(target), BodyRegion.Torso, new Vector3(0.1f, 1.0f, 0f), 0.005f,
                    FitStyle.Oversized, out Vector3 atHem));

                Assert.AreEqual(1.0f, atHem.y, 1e-3f, "아랫단은 대상 엉덩이 높이 그대로");
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, target.Root);
            }
        }

        /// <summary>왼쪽 아래팔 축(LowerArm -> Hand)을 두르는 원형 고리 정점을 아래팔 본에 묶은 몸 메시</summary>
        static void AddForearm(TestAvatar avatar, float radius)
        {
            Vector3 a = avatar[HumanBodyBones.LeftLowerArm].position, b = avatar[HumanBodyBones.LeftHand].position;
            var points = new List<(Vector3, Transform)>();
            for (float t = 0.1f; t < 0.95f; t += 0.1f)
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f;
                points.Add((Vector3.Lerp(a, b, t) + new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle)) * radius, avatar[HumanBodyBones.LeftLowerArm]));
            }
            TestRigBuilder.AddSkinnedMesh(avatar.Root.transform, "Forearm", points);
        }

        [Test]
        public void 아주_짧고_굵은_아래팔에서는_두_방식_모두_소매를_팔꿈치에_고정한_채_조금_늘이고_소맷부리도_옮긴다()
        {
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain);
            TestAvatar target = TestRigBuilder.CreateHumanoid("Target", NameStyle.Plain);
            try
            {
                // 아래팔: 원본 길이 0.25(x -0.45 -> -0.7), 반지름 0.04 / 대상 길이 0.05, 반지름 0.06 -> 길이 x0.2, 굵기 x1.5(눌림 7.5배)
                Transform hand = target[HumanBodyBones.LeftHand];
                hand.position = new Vector3(-0.5f, hand.position.y, hand.position.z);
                AddForearm(source, 0.04f);
                AddForearm(target, 0.06f);
                BodyCylinders Body(TestAvatar a) => BodyCylinders.Build(BodyPointCloud.Collect(AvatarRig.FromAnimator(a.Animator), new Transform[0], includeBodyChildren: true));
                BodyCylinders from = Body(source), to = Body(target);
                Vector3 sourceWrist = new Vector3(-0.7f, 1.45f + 0.04f, 0f);

                Assert.IsTrue(BodyCylinders.TryMap(from, to, BodyRegion.LeftLowerArm, sourceWrist, 0.005f, FitStyle.Oversized, out Vector3 over));
                Assert.IsTrue(BodyCylinders.TryMap(from, to, BodyRegion.LeftLowerArm, sourceWrist, 0.005f, FitStyle.Regular, out Vector3 regular));

                // 눌림 상한만 보면 늘임 = 굵기 / (팔 상한 4 x 길이 배율 0.2) = 1.875라 손목 너머 0.044까지 뻗지만,
                // 손 길이 한도에 막힌다: 가운데 손가락 본이 없어 손 길이 = 아래팔 0.05 x 0.4 = 0.02, 아래팔 몫은 그 절반 0.01.
                const float reach = 0.05f * BodyCylinders.HandToForearmFallback * BodyCylinders.CuffReachToHand * BodyCylinders.ArmReachShare;
                Assert.AreEqual(-0.5f - reach, over.x, 0.003f, "소매 끝은 손목을 손 길이 한도만큼만 넘는다(날개처럼 뻗지 않음)");
                Assert.AreEqual(over.x, regular.x, 1e-5f, "소매 상한은 두 방식이 같다");
                Vector3 shift = BodyCylinders.HandShift(from, to, true, FitStyle.Oversized);
                Assert.AreEqual(-reach, shift.x, 0.003f, "소맷부리는 소매 끝이 늘어난 만큼(위팔 부위가 없으면 아래팔만) 손 쪽으로");

                // 팔꿈치보다 몸 안쪽(t < 0) 점은 늘이지 않는다: 원본 팔꿈치 0.05 안쪽 -> 대상에서도 0.05 x 길이 배율이 아니라 원래 t 그대로
                Assert.IsTrue(BodyCylinders.TryMap(from, to, BodyRegion.LeftLowerArm, new Vector3(-0.4f, 1.49f, 0f), 0.005f, FitStyle.Oversized, out Vector3 inner));
                Assert.AreEqual(-0.45f + 0.05f * 0.2f, inner.x, 0.002f, "관절 안쪽은 늘이지 않고 길이 비율만");
                Assert.AreEqual(Vector3.zero, BodyCylinders.HandShift(from, Body(source), true, FitStyle.Oversized),
                    "아래팔이 같은 몸이면(늘임 1) 옮기지 않는다");
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, target.Root);
            }
        }

        [Test]
        public void 아랫단은_아무리_늘여도_허벅지_절반_아래로_내려가지_않는다()
        {
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain);
            TestAvatar target = TestRigBuilder.CreateHumanoid("Target", NameStyle.Plain);
            try
            {
                AddTorso(source, 0.1f, 0.1f);
                AddTorso(target, 0.3f, 0.3f);
                // 대상 몸통을 0.5 -> 0.1로 줄여 길이 x0.2, 굵기 x3: 오버핏 늘임 = 3 / 2 / 0.2 = 7.5배라 아랫단이 다리 아래까지 내려갈 수 있다.
                Transform neck = target[HumanBodyBones.Neck];
                neck.position = new Vector3(neck.position.x, 1.1f, neck.position.z);
                AvatarRig rig = AvatarRig.FromAnimator(target.Animator);
                float thigh = Vector3.Distance(target[HumanBodyBones.LeftUpperLeg].position, target[HumanBodyBones.LeftLowerLeg].position);
                BodyCylinders Body(TestAvatar a) => BodyCylinders.Build(BodyPointCloud.Collect(AvatarRig.FromAnimator(a.Animator), new Transform[0], includeBodyChildren: true));

                // 원통 첫 줄(엉덩이 높이)에도 몸통 굵기 고리를 둔다(테스트 몸통 고리는 1.05부터라 첫 줄이 엉덩이 축 정점(반지름 0)만 잡는다).
                var hipsRing = new List<(Vector3, Transform)>();
                for (int i = 0; i < 16; i++) hipsRing.Add((new Vector3(Mathf.Cos(i * Mathf.PI / 8f) * 0.3f, 1.002f, Mathf.Sin(i * Mathf.PI / 8f) * 0.3f), target[HumanBodyBones.Hips]));
                TestRigBuilder.AddSkinnedMesh(target.Root.transform, "HipsRing", hipsRing);
                Assert.IsTrue(BodyCylinders.TryMap(Body(source), Body(target), BodyRegion.Torso, new Vector3(0.1f, 1.0f, 0f), 0.005f, FitStyle.Oversized, out Vector3 atHem));

                float hips = target[HumanBodyBones.Hips].position.y;
                Assert.AreEqual(hips - thigh * BodyCylinders.HemReachToThigh, atHem.y, 0.01f, "아랫단은 엉덩이 아래 허벅지 절반에서 멈춘다");
                BodyCylinders sourceBody = Body(source), targetBody = Body(target);
                float atCap = BodyCylinders.RegionStretch(sourceBody, targetBody, BodyRegion.Torso, FitStyles.OversizedTorsoMaxSquash);
                Assert.AreEqual(atCap, BodyCylinders.RegionStretch(sourceBody, targetBody, BodyRegion.Torso, SquashCaps.MinCap), 1e-4f,
                    "한도에 막힌 부위는 상한을 낮춰도 늘임이 그대로라 형태 검사가 낮출 대상에서 뺀다");
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, target.Root);
            }
        }

        /// <summary>왼손 본 위치 둘레에 반지름 radius 고리(손 몸 정점)를 손 본에 묶어 넣는다.</summary>
        static void AddHand(TestAvatar avatar, float radius)
        {
            Transform hand = avatar[HumanBodyBones.LeftHand];
            var points = new List<(Vector3, Transform)>();
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f;
                points.Add((hand.position + new Vector3(-0.02f, Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius), hand));
            }
            TestRigBuilder.AddSkinnedMesh(avatar.Root.transform, "Hand", points);
        }

        [Test]
        public void 손_크기_배율은_아래팔_축에서_손_정점까지_거리의_비다()
        {
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain);
            TestAvatar target = TestRigBuilder.CreateHumanoid("Target", NameStyle.Plain);
            try
            {
                AddForearm(source, 0.03f);
                AddForearm(target, 0.03f);
                AddHand(source, 0.04f);
                AddHand(target, 0.06f);
                BodyCylinders Body(TestAvatar a) => BodyCylinders.Build(BodyPointCloud.Collect(AvatarRig.FromAnimator(a.Animator), new Transform[0], includeBodyChildren: true));

                // 굵은 앞발(inuinu)은 원본보다 손이 커서, 소맷부리 둘레를 이 배율로 키워 손이 소매를 뚫지 않게 한다.
                Assert.AreEqual(1.5f, BodyCylinders.HandScale(Body(source), Body(target), isLeft: true), 0.05f);
                Assert.AreEqual(0f, BodyCylinders.HandScale(Body(source), Body(target), isLeft: false), "손 정점이 없으면 잴 수 없다(0)");
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, target.Root);
            }
        }

        /// <summary>왼쪽 허벅지 축(UpperLeg -> LowerLeg)을 따라 반지름 radius 고리를 허벅지 본에 묶어 넣는다.</summary>
        static void AddThigh(TestAvatar avatar, float radius)
        {
            Transform hip = avatar[HumanBodyBones.LeftUpperLeg], knee = avatar[HumanBodyBones.LeftLowerLeg];
            var points = new List<(Vector3, Transform)>();
            for (float t = 0f; t <= 1.001f; t += 0.1f)
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f;
                points.Add((Vector3.Lerp(hip.position, knee.position, t) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius, hip));
            }
            TestRigBuilder.AddSkinnedMesh(avatar.Root.transform, "Thigh", points);
        }

        [Test]
        public void 허벅지_위쪽은_몸통_규칙으로_한_고리가_되고_깊은_곳은_허벅지를_따른다()
        {
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain);
            TestAvatar target = TestRigBuilder.CreateHumanoid("Target", NameStyle.Plain);
            try
            {
                AddTorso(source, 0.1f, 0.1f);
                AddTorso(target, 0.15f, 0.12f);
                AddThigh(source, 0.05f);
                AddThigh(target, 0.04f);
                BodyCylinders Body(TestAvatar a) => BodyCylinders.Build(BodyPointCloud.Collect(AvatarRig.FromAnimator(a.Animator), new Transform[0], includeBodyChildren: true));
                BodyCylinders sourceBody = Body(source), targetBody = Body(target);
                Vector3 hip = source[HumanBodyBones.LeftUpperLeg].position, knee = source[HumanBodyBones.LeftLowerLeg].position;
                Vector3 Around(float depth) => FitPlacement.ToRootFrame(source.Root.transform, Vector3.Lerp(hip, knee, depth) + Vector3.forward * 0.06f);
                Vector3 Map(BodyRegion region, float depth)
                {
                    Assert.IsTrue(BodyCylinders.TryMap(sourceBody, targetBody, region, Around(depth), 0.005f, FitStyle.Regular, out Vector3 p));
                    return p;
                }

                // 허벅지 위쪽: 몸통 규칙과 같은 위치(규칙이 섞이면 이웃 정점이 두 위치를 오가며 톱니가 됨, inuinu 뒤 아랫단).
                Assert.AreEqual(0f, (Map(BodyRegion.LeftUpperLeg, 0.1f) - Map(BodyRegion.Torso, 0.1f)).magnitude, 1e-5f);
                // 허벅지 깊은 곳(바지 다리): 몸통 원통이 아니라 허벅지 원통을 따라 대상 허벅지 축 가까이 놓인다.
                Vector3 targetKnee = FitPlacement.ToRootFrame(target.Root.transform, Vector3.Lerp(target[HumanBodyBones.LeftUpperLeg].position, target[HumanBodyBones.LeftLowerLeg].position, 0.9f));
                Vector3 deep = Map(BodyRegion.LeftUpperLeg, 0.9f);
                Assert.Greater((Map(BodyRegion.Torso, 0.9f) - deep).magnitude, 0.01f, "깊은 곳은 몸통 규칙과 다르다");
                Assert.Less(Vector3.ProjectOnPlane(deep - targetKnee, Vector3.up).magnitude, 0.1f, "대상 허벅지 축 가까이");
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, target.Root);
            }
        }

        [TestCase(FitStyle.Regular, BodyRegion.Torso, FitStyles.RegularTorsoMaxSquash)]
        [TestCase(FitStyle.Oversized, BodyRegion.Torso, FitStyles.OversizedTorsoMaxSquash)]
        [TestCase(FitStyle.Regular, BodyRegion.LeftLowerArm, FitStyles.ArmMaxSquash)]
        [TestCase(FitStyle.Oversized, BodyRegion.RightUpperArm, FitStyles.ArmMaxSquash)]
        [TestCase(FitStyle.Oversized, BodyRegion.LeftUpperLeg, 0f)]
        public void 맞춤_방식은_몸통_눌림_상한만_다르고_소매는_같으며_다리는_늘이지_않는다(FitStyle style, BodyRegion region, float expected)
        {
            Assert.AreEqual(expected, FitStyles.MaxSquash(style, region));
            Assert.Greater(FitStyles.RegularTorsoMaxSquash, FitStyles.OversizedTorsoMaxSquash, "정사이즈가 눌림을 더 허용한다(옷이 더 짧다)");
        }

        [Test]
        public void 원본이나_대상에_아래팔_부위가_없으면_소맷부리를_옮기지_않는다()
        {
            // 위팔은 대상이 6.25배 굵어(상한 4배 초과) 늘어나지만, 원본 몸에 아래팔 부위가 없어 아래팔 소매는 다시 배치되지 않는다.
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain, armRadius: 0.04f);
            TestAvatar target = TestRigBuilder.CreateHumanoid("Target", NameStyle.Plain, armRadius: 0.25f);
            try
            {
                Transform hand = target[HumanBodyBones.LeftHand];
                hand.position = new Vector3(-0.5f, hand.position.y, hand.position.z);
                AddForearm(target, 0.06f);
                BodyCylinders Body(TestAvatar a) => BodyCylinders.Build(BodyPointCloud.Collect(AvatarRig.FromAnimator(a.Animator), new Transform[0], includeBodyChildren: true));

                Assert.AreEqual(Vector3.zero, BodyCylinders.HandShift(Body(source), Body(target), true, FitStyle.Oversized));
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, target.Root);
            }
        }

        [Test]
        public void 두_겹_상의는_대상_몸_단면_모양을_따라_층_순서와_간격을_지키며_놓인다()
        {
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain);
            TestAvatar target = TestRigBuilder.CreateHumanoid("Target", NameStyle.Plain);
            try
            {
                target.Root.transform.position = new Vector3(2f, 0f, 0f);
                // 원본은 원형 몸통(0.1), 대상은 좌우로 넓은 타원 몸통(좌우 0.2, 앞뒤 0.1).
                // 본 굵기 배율은 단면을 원형으로만 키우므로, 앞뒤 방향에서 옷이 몸에 붙는지는 표면 간격 보존만 만족한다.
                AddTorso(source, 0.1f, 0.1f);
                AddTorso(target, 0.2f, 0.1f);
                Transform outfit = TwoLayerTop(source, inner: 0.105f, outer: 0.12f);
                FitAnalysis analysis = FitAnalysis.Run(new FitInput { Source = source.Animator, Outfit = outfit, Target = target.Animator });

                FitResult result = OutfitFitter.Fit(analysis, new FitOptions { FixPenetration = true, PreserveOffset = true });

                // 정점 순서: 안쪽 층 12개, 바깥 층 12개. 각 층의 0번은 좌우(x) 방향, 3번은 앞뒤(z) 방향.
                float[] radii = Radii((SkinnedMeshRenderer)result.Meshes.Single(m => m.name == "Layers"), target.Root.transform.position);
                AvatarRig targetRig = AvatarRig.FromAnimator(target.Animator);
                BodyCylinders targetBody = BodyCylinders.Build(BodyPointCloud.Collect(targetRig, new[] { result.Meshes.Single(m => m.name == "Layers").transform }, includeBodyChildren: true));
                float margin = SurfaceOffsetFitter.MarginOf(targetBody, RegionMeasurer.HipHeight(targetRig));
                Assert.AreEqual(0.2f + margin + 0.005f * 2f, radii[0], 0.01f, "좌우: 넓어진 몸 표면 + 여유 + 간격 x 2");
                Assert.AreEqual(0.1f + margin + 0.005f, radii[3], 0.01f, "앞뒤: 몸 두께 그대로라 원형으로 부풀지 않는다");
                Assert.Greater(radii[12], radii[0], "좌우에서 바깥 층이 안쪽 층 밖");
                Assert.Greater(radii[15], radii[3], "앞뒤에서 바깥 층이 안쪽 층 밖");
                StringAssert.Contains("간격 보존: Layers", result.ToReport());
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, target.Root);
            }
        }

        [Test]
        public void 표면_간격_보존을_끄면_풀어_주기를_켜도_관통_보정만_한_결과와_같다()
        {
            // AC6: 옵션을 끄면 다시 배치·겹침 정리·풀어 주기·늘어뜨리기가 끼어들지 않고 관통 보정만 한 결과와 같아야 한다.
            Vector3[] offOnly = FitTwoLayer(new FitOptions { FixPenetration = true }, out string offReport);
            Vector3[] offWithRelax = FitTwoLayer(new FitOptions { FixPenetration = true, PreserveOffset = false, Relax = true, Style = FitStyle.Regular }, out string relaxReport);

            CollectionAssert.AreEqual(offOnly, offWithRelax, "옵션을 끄면 다른 설정과 상관없이 같은 정점");
            foreach (string step in new[] { "간격 보존", "겹침 정리", "풀어 주기", "늘어뜨리기" })
            {
                StringAssert.DoesNotContain(step, offReport);
                StringAssert.DoesNotContain(step, relaxReport);
            }
        }

        /// <summary>원형 몸통 원본 -> 타원 몸통 대상으로 두 겹 상의를 맞춘 결과 정점(대상 루트 기준)과 보고</summary>
        static Vector3[] FitTwoLayer(FitOptions options, out string report)
        {
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain);
            TestAvatar target = TestRigBuilder.CreateHumanoid("Target", NameStyle.Plain);
            try
            {
                target.Root.transform.position = new Vector3(2f, 0f, 0f);
                AddTorso(source, 0.1f, 0.1f);
                AddTorso(target, 0.2f, 0.1f);
                Transform outfit = TwoLayerTop(source, inner: 0.105f, outer: 0.12f);
                FitAnalysis analysis = FitAnalysis.Run(new FitInput { Source = source.Animator, Outfit = outfit, Target = target.Animator });
                FitResult result = OutfitFitter.Fit(analysis, options);
                report = result.ToReport();
                var renderer = (SkinnedMeshRenderer)result.Meshes.Single(m => m.name == "Layers");
                Matrix4x4?[] matrices = PenetrationFixer.VertexMatrices(renderer, renderer.sharedMesh);
                return renderer.sharedMesh.vertices.Select((v, i) => matrices[i].Value.MultiplyPoint3x4(v) - target.Root.transform.position).ToArray();
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, target.Root);
            }
        }

        /// <summary>
        /// GA2: 코드로 만든 극단 비율 대상(치비: 작은 몸에 큰 머리, 장신, 저폴리 몸)에서도 원통 표·다시 배치·겹침 정리가 실패 없이 돌고 층 순서를 지킨다.
        /// </summary>
        [TestCase("Chibi", 0.5f, 0.8f, 16, 0.12f)]
        [TestCase("Tall", 1.6f, 0.2f, 16, 0.1f)]
        [TestCase("LowPoly", 1f, 0.2f, 5, 0.1f)]
        public void 극단_비율_대상에서도_실패_없이_층_순서를_지킨다(string name, float scale, float headWidth, int ringPoints, float torsoRadius)
        {
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain);
            TestAvatar target = TestRigBuilder.CreateHumanoid(name, NameStyle.Plain, scale: scale, headWidth: headWidth);
            try
            {
                target.Root.transform.position = new Vector3(3f, 0f, 0f);
                AddTorso(source, 0.1f, 0.1f);
                AddTorsoBetweenHipsAndNeck(target, torsoRadius, ringPoints);
                Transform outfit = TwoLayerTop(source, inner: 0.105f, outer: 0.12f);
                FitAnalysis analysis = FitAnalysis.Run(new FitInput { Source = source.Animator, Outfit = outfit, Target = target.Animator });

                FitResult result = OutfitFitter.Fit(analysis, new FitOptions { FixPenetration = true, PreserveOffset = true, Relax = true });

                var renderer = (SkinnedMeshRenderer)result.Meshes.Single(m => m.name == "Layers");
                Vector3[] vertices = renderer.sharedMesh.vertices;
                Assert.IsTrue(vertices.All(v => !float.IsNaN(v.x) && !float.IsInfinity(v.x)), "정점이 깨지지 않는다");
                StringAssert.Contains("간격 보존: Layers", result.ToReport());
                float[] radii = Radii(renderer, target.Root.transform.position);
                for (int i = 0; i < 12; i++)
                    Assert.Greater(radii[12 + i], radii[i], $"{name}: {i}번 방향에서 바깥 층이 안쪽 층 밖");
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, target.Root);
            }
        }

        /// <summary>엉덩이에서 목까지 Chest 본에 묶인 원형 몸통(고리마다 ringPoints개 정점, 고리 9개)</summary>
        static void AddTorsoBetweenHipsAndNeck(TestAvatar avatar, float radius, int ringPoints)
        {
            Transform chest = avatar[HumanBodyBones.Chest];
            Vector3 hips = avatar[HumanBodyBones.Hips].position, neck = avatar[HumanBodyBones.Neck].position;
            var points = new List<(Vector3, Transform)>();
            for (int k = 0; k <= 8; k++)
            {
                Vector3 center = Vector3.Lerp(hips, neck, k / 8f);
                for (int i = 0; i < ringPoints; i++)
                {
                    float a = i * Mathf.PI * 2f / ringPoints;
                    points.Add((center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius), chest));
                }
            }
            TestRigBuilder.AddSkinnedMesh(avatar.Root.transform, "Torso", points);
        }

        /// <summary>원본 몸통 축을 두르는 두 겹 고리(안쪽·바깥 층)를 원본과 같은 이름의 본 사슬(Hips -> Spine -> Chest)의 Chest에 묶은 상의</summary>
        static Transform TwoLayerTop(TestAvatar source, float inner, float outer)
        {
            var top = new GameObject("Top").transform;
            top.SetParent(source.Root.transform, false);
            Transform parent = top;
            foreach (HumanBodyBones bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest })
            {
                Transform copy = new GameObject(source[bone].name).transform;
                copy.SetParent(parent, false);
                copy.position = source[bone].position;
                parent = copy;
            }
            var points = new List<(Vector3, Transform)>();
            foreach (float r in new[] { inner, outer })
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                points.Add((new Vector3(Mathf.Cos(a) * r, 1.25f, Mathf.Sin(a) * r), parent));
            }
            TestRigBuilder.AddSkinnedMesh(top, "Layers", points);
            return top;
        }

        static float[] Radii(SkinnedMeshRenderer renderer, Vector3 center)
        {
            Mesh mesh = renderer.sharedMesh;
            Matrix4x4?[] matrices = PenetrationFixer.VertexMatrices(renderer, mesh);
            return mesh.vertices.Select((v, i) =>
            {
                Vector3 p = matrices[i].Value.MultiplyPoint3x4(v) - center;
                return new Vector2(p.x, p.z).magnitude;
            }).ToArray();
        }

        [Test]
        public void 몸_변이_뚫은_옷_삼각형은_바깥으로_밀어_풀린다()
        {
            var root = new GameObject("Root").transform;
            try
            {
                // 수평 옷 조각(법선 위)을 세로 몸 변이 2mm 위까지 뚫고 올라온다.
                var bone = new GameObject("Bone").transform;
                bone.SetParent(root, false);
                SkinnedMeshRenderer cloth = TestRigBuilder.AddSkinnedMesh(root, "Cloth", new List<(Vector3, Transform)>
                {
                    (new Vector3(-0.05f, 0f, -0.05f), bone), (new Vector3(0.05f, 0f, -0.05f), bone), (new Vector3(0f, 0f, 0.05f), bone),
                }, normals: new[] { Vector3.up, Vector3.up, Vector3.up });
                cloth.sharedMesh.SetIndices(new[] { 0, 2, 1 }, MeshTopology.Triangles, 0);
                var body = new EdgeGrid(new List<(Vector3, Vector3)> { (new Vector3(0f, -0.05f, 0f), new Vector3(0f, 0.002f, 0f)) });

                var (before, after) = ClothPierceResolver.Resolve(cloth, body, root, 0.001f, "Cloth_resolved");

                Assert.AreEqual(1, before);
                Assert.AreEqual(0, after);
                Assert.AreEqual("Cloth_resolved", cloth.sharedMesh.name, "정리한 새 메시로 교체");
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [Test]
        public void 고정한_정점은_겹침_정리에서_움직이지_않는다()
        {
            var root = new GameObject("Root").transform;
            try
            {
                // 통째로 배치한 끈처럼 고정한 옷 조각은 몸 변이 뚫어도 정점마다 밀지 않는다(밀면 모양이 구겨짐).
                var bone = new GameObject("Bone").transform;
                bone.SetParent(root, false);
                SkinnedMeshRenderer cloth = TestRigBuilder.AddSkinnedMesh(root, "Cloth", new List<(Vector3, Transform)>
                {
                    (new Vector3(-0.05f, 0f, -0.05f), bone), (new Vector3(0.05f, 0f, -0.05f), bone), (new Vector3(0f, 0f, 0.05f), bone),
                }, normals: new[] { Vector3.up, Vector3.up, Vector3.up });
                cloth.sharedMesh.SetIndices(new[] { 0, 2, 1 }, MeshTopology.Triangles, 0);
                Vector3[] original = cloth.sharedMesh.vertices;
                var body = new EdgeGrid(new List<(Vector3, Vector3)> { (new Vector3(0f, -0.05f, 0f), new Vector3(0f, 0.002f, 0f)) });

                var (before, after) = ClothPierceResolver.Resolve(cloth, body, root, 0.001f, "Cloth_locked", new[] { true, true, true });

                Assert.AreEqual(1, before);
                Assert.AreEqual(1, after, "고정한 면은 그대로 남는다");
                CollectionAssert.AreEqual(original, cloth.sharedMesh.vertices);
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }
    }
}
