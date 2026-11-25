using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    public class ScaleProfileTests
    {
        [Test]
        public void 잴_수_없으면_대체값을_쓴다()
        {
            Assert.AreEqual(0.7f, ScaleProfile.SafeRatio(0f, 1f, 0.7f));
            Assert.AreEqual(0.7f, ScaleProfile.SafeRatio(1f, 0f, 0.7f));
        }

        [Test]
        public void 비율은_허용_범위로_제한한다()
        {
            Assert.AreEqual(ScaleProfile.MaxRatio, ScaleProfile.SafeRatio(1000f, 1f, 1f));
            Assert.AreEqual(ScaleProfile.MinRatio, ScaleProfile.SafeRatio(0.0001f, 1f, 1f));
        }

        [Test]
        public void 머리_아래_본은_머리_비율_나머지는_몸_비율()
        {
            TestAvatar avatar = TestRigBuilder.CreateHumanoid("Rig", NameStyle.Plain);
            try
            {
                AvatarRig rig = AvatarRig.FromAnimator(avatar.Animator);
                var profile = new ScaleProfile(1f, 2f);
                var hair = new GameObject("Hair").transform;
                hair.SetParent(avatar[HumanBodyBones.Head], false);

                Assert.AreEqual(2f, profile.RatioFor(rig, hair));
                Assert.AreEqual(1f, profile.RatioFor(rig, avatar[HumanBodyBones.RightHand]));
                Assert.AreEqual(1f, profile.RatioFor(rig, avatar[HumanBodyBones.Chest]));
            }
            finally
            {
                TestRigBuilder.Destroy(avatar.Root);
            }
        }

        [Test]
        public void 치비_아바타는_몸보다_머리_비율이_크게_측정된다()
        {
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain, scale: 1f, headWidth: 0.2f);
            TestAvatar chibi = TestRigBuilder.CreateHumanoid("Chibi", NameStyle.VRoid, scale: 0.5f, headWidth: 0.8f);
            try
            {
                ScaleProfile measured = RegionMeasurer.Measure(AvatarRig.FromAnimator(source.Animator), null,
                    AvatarRig.FromAnimator(chibi.Animator), out bool headMeasured);

                Assert.IsTrue(headMeasured);
                Assert.AreEqual(0.5f, measured.Body, 1e-3f);
                Assert.AreEqual(2f, measured.Head, 1e-3f, "머리 폭 0.8*0.5=0.4 / 0.2");
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, chibi.Root);
            }
        }

        [Test]
        public void FBX_단위_스케일이_100이어도_실제_크기로_잰다()
        {
            TestAvatar normal = TestRigBuilder.CreateHumanoid("Normal", NameStyle.Plain, headWidth: 0.2f);
            TestAvatar centimeter = TestRigBuilder.CreateHumanoid("Centimeter", NameStyle.Plain, headWidth: 0.2f, unitScale: 100f);
            try
            {
                float expected = RegionMeasurer.RegionWidth(AvatarRig.FromAnimator(normal.Animator), null, HumanBodyBones.Head);
                float actual = RegionMeasurer.RegionWidth(AvatarRig.FromAnimator(centimeter.Animator), null, HumanBodyBones.Head);

                Assert.AreEqual(0.2f, expected, 1e-4f);
                Assert.AreEqual(expected, actual, 1e-4f);
            }
            finally
            {
                TestRigBuilder.Destroy(normal.Root, centimeter.Root);
            }
        }

        [Test]
        public void 파츠마다_Armature가_따로_있어도_같은_이름의_메인_본_구역으로_잰다()
        {
            TestAvatar avatar = TestRigBuilder.CreateHumanoid("Parts", NameStyle.Plain);
            try
            {
                // Modular Avatar 방식 파츠: 메인과 같은 이름의 본 사슬을 따로 가진 모자
                var part = new GameObject("Hat").transform;
                part.SetParent(avatar.Root.transform, false);
                Transform parent = new GameObject("Armature").transform;
                parent.SetParent(part, false);
                foreach (HumanBodyBones bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
                             HumanBodyBones.Neck, HumanBodyBones.Head })
                {
                    Transform copy = new GameObject(avatar[bone].name).transform;
                    copy.SetParent(parent, false);
                    copy.position = avatar[bone].position;
                    parent = copy;
                }
                Vector3 head = avatar[HumanBodyBones.Head].position;
                TestRigBuilder.AddSkinnedMesh(part, "HatMesh", new List<(Vector3, Transform)>
                {
                    (head + Vector3.left * 0.5f, parent),
                    (head + Vector3.right * 0.5f, parent),
                });

                float width = RegionMeasurer.RegionWidth(AvatarRig.FromAnimator(avatar.Animator), null, HumanBodyBones.Head);

                Assert.AreEqual(1.0f, width, 1e-4f, "파츠 모자 정점(폭 1.0)이 머리 구역에 포함되어야 한다");
            }
            finally
            {
                TestRigBuilder.Destroy(avatar.Root);
            }
        }
        [Test]
        public void 귀처럼_Humanoid가_아닌_하위_본의_정점은_머리_구역에서_뺀다()
        {
            TestAvatar avatar = TestRigBuilder.CreateHumanoid("Kemono", NameStyle.Plain, headWidth: 0.2f);
            try
            {
                Transform ear = new GameObject("Ear_L").transform;
                ear.SetParent(avatar[HumanBodyBones.Head], false);
                Vector3 tip = avatar[HumanBodyBones.Head].position + new Vector3(-0.3f, 1.0f, 0f);
                TestRigBuilder.AddSkinnedMesh(avatar.Root.transform, "EarMesh", new List<(Vector3, Transform)> { (tip, ear), (tip + Vector3.up, ear) });

                bool measured = RegionMeasurer.TryRegionBounds(AvatarRig.FromAnimator(avatar.Animator), null, HumanBodyBones.Head, out Bounds head);

                Assert.IsTrue(measured);
                Assert.AreEqual(0.2f, head.size.x, 1e-4f, "귀 끝이 폭에 들어가면 안 된다");
                Assert.AreEqual(0.2f, head.size.y, 1e-4f, "귀 끝이 높이에 들어가면 안 된다");
            }
            finally
            {
                TestRigBuilder.Destroy(avatar.Root);
            }
        }

        [Test]
        public void 원통_단면_정점의_바깥_윤곽_반지름은_원의_반지름이다()
        {
            var points = new List<Vector3>();
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f;
                points.Add(new Vector3(0.3f, Mathf.Cos(angle) * 0.05f, Mathf.Sin(angle) * 0.05f));
            }

            Assert.AreEqual(0.05f, BodySegmentMeasurer.OutlineRadius(points, Vector3.zero, Vector3.right), 1e-4f);
            Assert.AreEqual(0f, BodySegmentMeasurer.OutlineRadius(new List<Vector3>(), Vector3.zero, Vector3.right));
        }

        [Test]
        public void 몸통_마디의_길이_축은_엉덩이에서_목_방향이다()
        {
            TestAvatar avatar = TestRigBuilder.CreateHumanoid("Torso", NameStyle.Plain);
            try
            {
                // Hips와 Spine이 1mm 옆으로 붙은 리그: 마디 자체 방향(옆)이 아니라 몸통 방향(위)을 축으로 써야 한다.
                Transform spine = avatar[HumanBodyBones.Spine], chest = avatar[HumanBodyBones.Chest];
                Vector3 chestPosition = chest.position;
                spine.position = avatar[HumanBodyBones.Hips].position + Vector3.right * 0.001f;
                chest.position = chestPosition;
                AvatarRig rig = AvatarRig.FromAnimator(avatar.Animator);

                Assert.IsTrue(BodySegmentMeasurer.TryGetAxis(rig, HumanBodyBones.Hips, out Vector3 axis));
                Vector3 expected = avatar[HumanBodyBones.Neck].position - avatar[HumanBodyBones.Hips].position;
                Assert.Less(Vector3.Angle(expected, axis), 0.01f);
            }
            finally
            {
                TestRigBuilder.Destroy(avatar.Root);
            }
        }

        [Test]
        public void 관절_근처_정점은_빼고_마디_가운데_단면으로_굵기를_잰다()
        {
            // 마디 길이 1: 가운데(0.5) 고리 반지름 0.05 16점, 관절 근처(0.05) 어깨처럼 넓은 고리 반지름 0.3 16점
            var points = new List<Vector3>();
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8f;
                var ring = new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle));
                points.Add(new Vector3(0.5f, 0f, 0f) + ring * 0.05f);
                points.Add(new Vector3(0.05f, 0f, 0f) + ring * 0.3f);
            }

            Assert.AreEqual(0.05f, BodySegmentMeasurer.OutlineRadius(points, Vector3.zero, Vector3.right), 1e-4f);
        }

        [Test]
        public void 축_가까이_몰린_정점이_많아도_바깥_윤곽으로_굵기를_잰다()
        {
            // 바깥 윤곽(반지름 0.1) 정점 10개와 축 가까이(0.01) 몰린 안쪽 정점 10개: 평균이면 0.055로 작게 나온다.
            var points = new List<Vector3>();
            for (int i = 0; i < 10; i++)
            {
                float angle = i * Mathf.PI / 5f;
                var ring = new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle));
                points.Add(new Vector3(0.3f, 0f, 0f) + ring * 0.1f);
                points.Add(new Vector3(0.3f, 0f, 0f) + ring * 0.01f);
            }

            Assert.AreEqual(0.1f, BodySegmentMeasurer.OutlineRadius(points, Vector3.zero, Vector3.right), 1e-4f);
        }

        [Test]
        public void 팔이_두꺼운_대상은_위팔_굵기_비율이_크게_측정된다()
        {
            TestAvatar thin = TestRigBuilder.CreateHumanoid("Thin", NameStyle.Plain, armRadius: 0.04f);
            TestAvatar thick = TestRigBuilder.CreateHumanoid("Thick", NameStyle.VRoid, scale: 0.5f, armRadius: 0.16f);
            try
            {
                var ratios = BodySegmentMeasurer.Measure(AvatarRig.FromAnimator(thin.Animator), null, AvatarRig.FromAnimator(thick.Animator), 1f);

                Assert.AreEqual(2f, ratios[HumanBodyBones.LeftUpperArm].Thickness, 1e-3f, "0.16*0.5 / 0.04");
                Assert.AreEqual(0.5f, ratios[HumanBodyBones.LeftUpperArm].Length, 1e-3f);
                Assert.IsFalse(ratios.ContainsKey(HumanBodyBones.Spine), "정점이 없는 마디는 재지 않는다");
            }
            finally
            {
                TestRigBuilder.Destroy(thin.Root, thick.Root);
            }
        }

        [Test]
        public void 거의_겹친_본_사이_마디는_길이를_믿지_않고_몸_비율을_쓴다()
        {
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain, armRadius: 0.04f);
            TestAvatar target = TestRigBuilder.CreateHumanoid("Target", NameStyle.VRoid, scale: 0.5f, armRadius: 0.04f);
            try
            {
                // 원본의 아래팔 본을 위팔 본 바로 옆(1mm)으로 옮긴다: 위팔 길이 0.001
                source[HumanBodyBones.LeftLowerArm].position = source[HumanBodyBones.LeftUpperArm].position + Vector3.left * 0.001f;

                var ratios = BodySegmentMeasurer.Measure(AvatarRig.FromAnimator(source.Animator), null, AvatarRig.FromAnimator(target.Animator), 0.5f);

                Assert.AreEqual(0.5f, ratios[HumanBodyBones.LeftUpperArm].Length, 1e-4f);
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, target.Root);
            }
        }

        [Test]
        public void 굵기는_몸_비율의_10배_길이는_2배_안으로_제한한다()
        {
            Assert.AreEqual(5f, BodySegmentMeasurer.LimitToBody(20f, 0.5f, BodySegmentMeasurer.MaxThicknessDeviation), 1e-4f);
            Assert.AreEqual(0.05f, BodySegmentMeasurer.LimitToBody(0.01f, 0.5f, BodySegmentMeasurer.MaxThicknessDeviation), 1e-4f);
            Assert.AreEqual(1f, BodySegmentMeasurer.LimitToBody(20f, 0.5f, BodySegmentMeasurer.MaxLengthDeviation), 1e-4f);
            Assert.AreEqual(0.7f, BodySegmentMeasurer.LimitToBody(0.7f, 0.5f, BodySegmentMeasurer.MaxLengthDeviation), 1e-4f);
        }

        [Test]
        public void 같이_옮길_다른_의상의_정점은_원본_팔_굵기에서_뺀다()
        {
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain, armRadius: 0.04f);
            TestAvatar target = TestRigBuilder.CreateHumanoid("Target", NameStyle.VRoid, armRadius: 0.04f);
            try
            {
                // 원본이 위팔에 두꺼운 재킷(반지름 0.2, 팔을 두르는 고리 16점)을 겹쳐 입고 있다.
                var jacket = new GameObject("Jacket").transform;
                jacket.SetParent(source.Root.transform, false);
                Vector3 mid = Vector3.Lerp(source[HumanBodyBones.LeftUpperArm].position, source[HumanBodyBones.LeftLowerArm].position, 0.5f);
                var sleeve = new List<(Vector3, Transform)>();
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8f;
                    sleeve.Add((mid + new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle)) * 0.2f, source[HumanBodyBones.LeftUpperArm]));
                }
                TestRigBuilder.AddSkinnedMesh(jacket, "JacketMesh", sleeve);
                AvatarRig sourceRig = AvatarRig.FromAnimator(source.Animator), targetRig = AvatarRig.FromAnimator(target.Animator);

                BodyPointCloud all = BodyPointCloud.Collect(sourceRig, null);
                var mixed = BodySegmentMeasurer.Measure(all, BodyPointCloud.Collect(targetRig, null), 1f);
                var clean = BodySegmentMeasurer.Measure(BodyPointCloud.Collect(sourceRig, new[] { jacket }), BodyPointCloud.Collect(targetRig, null), 1f);
                var filtered = BodySegmentMeasurer.Measure(all.Excluding(new[] { jacket }), BodyPointCloud.Collect(targetRig, null), 1f);

                Assert.Less(mixed[HumanBodyBones.LeftUpperArm].Thickness, 0.9f, "재킷이 섞이면 원본 팔이 굵게 재져 비율이 작아진다");
                Assert.AreEqual(1f, clean[HumanBodyBones.LeftUpperArm].Thickness, 1e-3f);
                Assert.AreEqual(1f, filtered[HumanBodyBones.LeftUpperArm].Thickness, 1e-3f, "메시를 다시 읽지 않고 걸러내도 같은 결과");
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, target.Root);
            }
        }

        [Test]
        public void 몸통_마디_길이는_척추_본_위치와_상관없이_엉덩이에서_목까지_비율을_쓴다()
        {
            TestAvatar source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain, armRadius: 0.04f);
            TestAvatar target = TestRigBuilder.CreateHumanoid("Target", NameStyle.VRoid, scale: 0.5f, armRadius: 0.04f);
            try
            {
                // 대상은 척추 본을 가슴 바로 아래에 둔 리그(본마다 재면 척추가 짧게 잡힘)이고, 목도 높여 몸통이 몸 비율(0.5)보다 길다.
                Transform spine = target[HumanBodyBones.Spine], chest = target[HumanBodyBones.Chest], neck = target[HumanBodyBones.Neck];
                Vector3 chestPosition = chest.position;
                spine.position = Vector3.Lerp(spine.position, chestPosition, 0.8f);
                chest.position = chestPosition;
                neck.position += Vector3.up * 0.1f;
                float torso = Vector3.Distance(target[HumanBodyBones.Hips].position, neck.position)
                    / Vector3.Distance(source[HumanBodyBones.Hips].position, source[HumanBodyBones.Neck].position);
                Assert.Greater(torso, 0.55f, "몸통 비율이 몸 비율과 달라야 이 검증이 의미 있다");
                foreach (TestAvatar a in new[] { source, target })
                {
                    foreach (HumanBodyBones bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest })
                    {
                        Transform t = a[bone];
                        TestRigBuilder.AddSkinnedMesh(a.Root.transform, bone + "Mesh", new List<(Vector3, Transform)>
                        {
                            (t.position + Vector3.left * 0.1f, t), (t.position + Vector3.right * 0.1f, t),
                        });
                    }
                }

                var ratios = BodySegmentMeasurer.Measure(AvatarRig.FromAnimator(source.Animator), null, AvatarRig.FromAnimator(target.Animator), 0.5f);

                foreach (HumanBodyBones bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest })
                    Assert.AreEqual(torso, ratios[bone].Length, 1e-4f, $"{bone}: 몸통 전체(Hips~Neck) 길이 비율");
            }
            finally
            {
                TestRigBuilder.Destroy(source.Root, target.Root);
            }
        }

        [Test]
        public void 꺼진_메시는_몸_측정과_몸_표면에서_뺀다()
        {
            TestAvatar avatar = TestRigBuilder.CreateHumanoid("Avatar", NameStyle.Plain, headWidth: 0.2f);
            try
            {
                // 벗겨 둔 큰 모자: 머리 본에 묶였지만 꺼져 있다.
                Transform head = avatar[HumanBodyBones.Head];
                SkinnedMeshRenderer hidden = TestRigBuilder.AddSkinnedMesh(avatar.Root.transform, "HiddenHat", new List<(Vector3, Transform)>
                {
                    (head.position + Vector3.left, head), (head.position + Vector3.right, head),
                });
                hidden.gameObject.SetActive(false);

                bool measured = RegionMeasurer.TryRegionBounds(AvatarRig.FromAnimator(avatar.Animator), null, HumanBodyBones.Head, out Bounds box);

                Assert.IsTrue(measured);
                Assert.AreEqual(0.2f, box.size.x, 1e-4f, "꺼진 모자 정점이 머리 폭에 들어가면 안 된다");
            }
            finally
            {
                TestRigBuilder.Destroy(avatar.Root);
            }
        }
    }
}
