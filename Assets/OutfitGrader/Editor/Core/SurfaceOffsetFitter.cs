using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 상의 정점을 원본 몸 표면과의 간격·층 순서를 보존해 대상 몸에 다시 배치한다(표면 간격 보존 맞춤, docs/prd/surface-offset-fitting.md).
    /// 원본 정점을 원본 몸 부위 원통 좌표(길이 위치·둘레 각도·축과의 거리)로 바꾸고, 대상 몸의 같은 좌표에 대상 표면 기준으로 놓는다.
    /// 마디 경계(어깨·팔꿈치·허리)는 정점의 스킨 가중치로 부위 결과를 섞어 스키닝처럼 이어지게 하고,
    /// 머리(후드)·손 등 범위 밖 본에 걸린 가중치만큼은 기존 맞추기 결과 위치를 그대로 쓴다.
    /// </summary>
    public static class SurfaceOffsetFitter
    {
        /// <summary>이보다 적게 움직이는 정점은 그대로 둔다(월드 단위).</summary>
        const float MinMove = 1e-5f;
        /// <summary>섞은 스킨 행렬의 행렬식이 이보다 작으면(크기 0으로 숨긴 본 등) 역행렬을 믿을 수 없어 그 정점은 그대로 둔다.</summary>
        public const float MinDeterminant = 1e-12f;
        /// <summary>
        /// 다시 배치할 때 대상 몸 표면과 남길 여유 간격: 대상 몸통 원통 반지름(표 중앙값) 대비 비율(Lumelana 81.8mm에서 약 2.7mm).
        /// 엉덩이 높이 기준은 다리가 짧은 치비에서 몸에 비해 너무 작아(inuinu 0.87mm) 성긴 몸 면이 옷을 뚫었다.
        /// </summary>
        public const float MarginToTorsoRadius = 0.0335f;
        /// <summary>형태가 무너진 부위의 눌림 상한을 낮춰 다시 배치하는 최대 횟수</summary>
        public const int MaxFormRetries = 4;

        /// <summary>정점마다 공통으로 쓰는 배치 설정(원본·대상 몸, 여유 간격, 눌림 상한, 옷 본별 부위와 손 쪽 이동)</summary>
        readonly struct Placement
        {
            public readonly BodyCylinders Source, Target;
            public readonly Transform TargetRoot;
            public readonly float Margin;
            public readonly SquashCaps Caps;
            public readonly BodyRegion[] BoneRegions;
            /// <summary>범위 밖 본(손 등)에 걸린 정점을 지금 위치에서 더 옮길 월드 거리. 소맷부리가 늘어난 소매 끝을 따라가게 한다.</summary>
            public readonly Vector3[] BoneShifts;

            public Placement(BodyCylinders source, BodyCylinders target, Transform targetRoot, float margin, SquashCaps caps,
                BodyRegion[] boneRegions, Vector3[] boneShifts)
            {
                Source = source; Target = target; TargetRoot = targetRoot; Margin = margin; Caps = caps;
                BoneRegions = boneRegions; BoneShifts = boneShifts;
            }
        }

        /// <summary>
        /// 관통 밀어내기·꼬리 구멍 가장자리 당기기의 최대 거리: 대상 몸통 원통 반지름 대비 비율. Lumelana(반지름 81.8mm)에서 엉덩이 높이 기준(36mm)과 같게 정했다.
        /// 엉덩이 높이 기준은 다리가 짧은 치비에서 너무 작아(inuinu 11.6mm) 큰 삼각형 옷의 꼬리 구멍 가장자리가 꼬리에 닿지 못했다(PRD G1).
        /// </summary>
        public const float MaxPushToTorsoRadius = 0.44f;

        /// <summary>관통 밀어내기 최대 거리(월드 단위). 몸통 원통이 있으면 그 굵기, 없으면 엉덩이 높이 기준.</summary>
        public static float MaxPushOf(BodyCylinders target, float hipHeight)
        {
            float torsoRadius = target != null ? target.MedianRadius(BodyRegion.Torso) : 0f;
            return torsoRadius > 0f ? torsoRadius * MaxPushToTorsoRadius : hipHeight * PenetrationFixer.MaxPushToHipHeight;
        }

        /// <summary>다시 배치 여유 간격(월드 단위). 몸통 원통이 있으면 그 굵기, 없으면 엉덩이 높이 기준.</summary>
        public static float MarginOf(BodyCylinders target, float hipHeight)
        {
            float torsoRadius = target != null ? target.MedianRadius(BodyRegion.Torso) : 0f;
            return torsoRadius > 0f ? torsoRadius * MarginToTorsoRadius : hipHeight * PenetrationFixer.MarginToHipHeight;
        }

        /// <summary>
        /// 복제본 옷 메시(clone)를 원본 옷(source) 정점 기준으로 다시 배치한 새 메시로 교체한다. 다시 배치한 정점 수를 돌려준다.
        /// clone과 source는 같은 메시 정점 순서를 가진다(복제본이므로).
        /// isFullyPlaced: 가중치 전부가 부위로 옮겨진 정점(범위 밖 본 가중치가 없음)과 통째로 배치한 끈 정점. 뒤따르는 밀어내기에서 이 정점은 건너뛴다.
        /// </summary>
        /// <param name="style">맞춤 방식. 부위별 한쪽 눌림 상한(FitStyles.MaxSquash)을 넘으면 옷 길이를 남긴다.</param>
        /// <param name="relax">참이면 뒤따르는 풀어 주기(GarmentRelaxer)에 넘길 정점별 부위를 보고에 담는다.</param>
        /// <param name="report">보고용 요약(형태 검사, 끈·후드·소맷부리 처리)과 끈 여부·풀 부위</param>
        public static int Fit(FitAnalysis analysis, SkinnedMeshRenderer clone, SkinnedMeshRenderer source,
            BodyCylinders sourceBody, BodyCylinders targetBody, float margin, FitStyle style, bool relax, string meshName, out bool[] isFullyPlaced,
            out PlacementReport report)
        {
            isFullyPlaced = null;
            report = new PlacementReport();
            Mesh mesh = clone.sharedMesh;
            if (mesh == null || source.sharedMesh == null || source.sharedMesh.vertexCount != mesh.vertexCount) return 0;

            BoneMatch[] boneMatches = source.bones.Select(b => HumanMatchOfOutfitBone(analysis, b)).ToArray();
            BodyRegion[] boneRegions = boneMatches.Select(m => m != null ? BodyCylinders.RegionOf(m.HumanBone) : BodyRegion.None).ToArray();
            if (boneRegions.All(r => r == BodyRegion.None)) return 0;

            var frame = new FitFrame(analysis, clone, source, sourceBody, targetBody, margin, boneMatches, boneRegions);
            SquashCaps caps = SquashCaps.For(style);
            Vector3[] deltas = PlaceKeepingForm(frame, ref caps, out isFullyPlaced, out int moved, out bool[] isStrip);
            bool[] placedStrip = isStrip;
            report.IsStrip = isStrip;
            report.Strips = frame.StripMembers.Count(members => members.Any(v => placedStrip[v]));
            report.FormNote = caps.Describe(style);
            report.HoodShift = frame.HoodShift;
            report.CuffScale = frame.CuffAttached > 0 ? frame.CuffScale : 1f;
            report.CuffGirth = frame.CuffAttached > 0 ? frame.CuffGirth : 0f;
            if (moved == 0) return 0;

            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] += deltas[i];
            if (relax) FillRelax(frame, caps, isStrip, report);
            Mesh placed = Object.Instantiate(mesh);
            placed.name = meshName;
            placed.vertices = vertices;
            placed.RecalculateBounds();
            PenetrationFixer.ReplaceMesh(clone, placed);
            return moved;
        }

        /// <summary>
        /// 풀어 주기에 넘길 정점별 부위: 주 본이 몸통·팔인 정점(다른 부위 가중치가 섞여도)은 그 부위, 끈·후드(머리·목)·소맷부리(손)·부위 밖 정점은 None(고정).
        /// 섞인 정점까지 풀어야 움직이는 곳과 고정한 곳 경계가 꺾이지 않는다.
        /// </summary>
        /// <summary>풀어 주기에 넘길 정점별 부위와 소매 쉼 거리를 보고에 담는다.</summary>
        static void FillRelax(FitFrame frame, SquashCaps caps, bool[] isStrip, PlacementReport report)
        {
            report.RelaxRegion = RelaxRegions(frame, isStrip);
            report.RelaxRestLength = SleeveRestLength(frame, caps, report.RelaxRegion);
            // 늘인 뒤에도 몸통이 한쪽으로 눌린 정도(굵기 배율 / 길이 배율)가 크면 남는 천이 옆구리에서 층으로 겹친다.
            bool hasTorso = BodyCylinders.TryAxisScales(frame.SourceBody, frame.TargetBody, BodyRegion.Torso, caps.Of(BodyRegion.Torso), out float length, out float radial);
            report.NeedsDrape = hasTorso && length > 0f && radial / length >= GarmentDraper.MinTorsoSquash;
        }

        static BodyRegion[] RelaxRegions(FitFrame frame, bool[] isStrip)
        {
            var result = new BodyRegion[frame.Vertices.Length];
            for (int i = 0; i < result.Length; i++)
            {
                bool hasBone = i < frame.Weights.Length && frame.Weights[i].boneIndex0 < frame.BoneRegions.Length;
                bool hasSource = !float.IsInfinity(frame.SourceRoot[i].x);
                if (!hasBone || !hasSource || isStrip[i]) continue;
                BodyRegion region = frame.BoneRegions[frame.Weights[i].boneIndex0];
                // 허벅지 주 본 정점 중 몸통 규칙으로 놓인 위쪽(BodyCylinders.TryMap, 깊이 HipRuleUntil까지)만 몸통으로 푼다(하의 다리는 고정).
                bool isThigh = region == BodyRegion.LeftUpperLeg || region == BodyRegion.RightUpperLeg;
                bool isHipThigh = isThigh && frame.SourceBody.Depth(region, frame.SourceRoot[i]) <= BodyCylinders.HipRuleUntil;
                BodyRegion relaxAs = isHipThigh ? BodyRegion.Torso : isThigh ? BodyRegion.None : region;
                // 소맷부리(손 본)는 아래팔 소매와 함께 풀어야 이음매가 벌어지지 않는다(고정하면 소매만 끌려가 끊어짐, 실측).
                if (frame.BoneMatches[frame.Weights[i].boneIndex0] is BoneMatch hand)
                {
                    if (hand.HumanBone == HumanBodyBones.LeftHand) relaxAs = BodyRegion.LeftLowerArm;
                    else if (hand.HumanBone == HumanBodyBones.RightHand) relaxAs = BodyRegion.RightLowerArm;
                }
                bool isBodyRegion = relaxAs == BodyRegion.Torso || IsArm(relaxAs);
                result[i] = isBodyRegion ? relaxAs : BodyRegion.None;
            }
            return result;
        }

        /// <summary>
        /// 소매(팔 부위로 풀) 정점 쌍의 쉼 거리 함수: 원본 변 벡터를 두 정점 각자의 팔 마디 변환(축 방향 길이 배율·둘레 굵기 배율·대상 축 회전)으로 바꾼 평균 길이.
        /// 지금 변 길이를 쉼으로 두면 길이로 눌린 소매 주름이 그대로 남아(실측), 원본 모양의 비율을 목표로 당겨 매끈하게 편다.
        /// 절대 위치가 아닌 변 벡터에 변환을 적용해야 팔꿈치·어깨처럼 마디가 바뀌는 변이 어긋나지 않는다(데모 DemoGarmentSim과 같은 방식).
        /// </summary>
        /// 소맷부리(손 본)도 아래팔 비율로 함께 당긴다. 소맷부리만 지금 크기로 두면 소매와 이어지는 곳에 톱니가 다시 섰다(실측 관통 193 -> 216).
        /// 이때 소맷부리는 손을 덮게 넓힌 크기보다 조금 좁아지지만, 원래 위치에 붙여 두는 힘과 몸 밀기로 앞발을 덮는다(캡처 확인).
        static System.Func<int, int, float> SleeveRestLength(FitFrame frame, SquashCaps caps, BodyRegion[] relaxRegion)
        {
            var maps = new Dictionary<BodyRegion, (Vector3 from, Vector3 to, float along, float across)>();
            foreach (BodyRegion arm in new[] { BodyRegion.LeftUpperArm, BodyRegion.LeftLowerArm, BodyRegion.RightUpperArm, BodyRegion.RightLowerArm })
            {
                bool canScale = BodyCylinders.TryAxisScales(frame.SourceBody, frame.TargetBody, arm, caps.Of(arm), out float along, out float across)
                    & frame.SourceBody.TryAxis(arm, out Vector3 fromAxis) & frame.TargetBody.TryAxis(arm, out Vector3 toAxis);
                if (canScale) maps[arm] = (fromAxis, toAxis, along, across);
            }
            Vector3[] source = frame.SourceRoot;
            return (a, b) =>
            {
                bool isShaped = maps.TryGetValue(relaxRegion[a], out var ma) & maps.TryGetValue(relaxRegion[b], out var mb);
                if (!isShaped) return -1f;
                Vector3 d = source[b] - source[a];
                Vector3 va = PartAttach.AxisStretch(d, ma.from, ma.to, ma.along, ma.across), vb = PartAttach.AxisStretch(d, mb.from, mb.to, mb.along, mb.across);
                return ((va + vb) * 0.5f).magnitude;
            };
        }

        static bool IsArm(BodyRegion r) => r == BodyRegion.LeftUpperArm || r == BodyRegion.LeftLowerArm || r == BodyRegion.RightUpperArm || r == BodyRegion.RightLowerArm;

        /// <summary>한 옷 메시를 다시 배치할 때 반복마다 바뀌지 않는 값(정점·행렬·원본 위치·삼각형 부위)</summary>
        sealed class FitFrame
        {
            public readonly FitAnalysis Analysis;
            public readonly BodyCylinders SourceBody, TargetBody;
            public readonly float Margin;
            public readonly BoneMatch[] BoneMatches;
            public readonly BodyRegion[] BoneRegions;
            public readonly Vector3[] Vertices, SourceWorld, SourceRoot;
            public readonly BoneWeight[] Weights;
            public readonly Matrix4x4?[] CloneMatrices;
            public readonly int[] Triangles;
            /// <summary>정점마다 끈 번호(-1: 끈 아님), 끈마다 매달린 지점 정점과 따르는 몸 부위(StripChains)</summary>
            public readonly int[] StripOf, StripAnchors;
            public readonly BodyRegion[] StripRegions;
            /// <summary>머리·목 본에만 묶인 정점 덩어리(후드 등)마다 정점과, 덩어리 밖 정점과 삼각형 변으로 이어진 이음매 쌍</summary>
            public readonly (int[] members, (int part, int body)[] seams)[] HoodParts;
            /// <summary>마지막 배치에서 덩어리를 옮긴 가장 큰 거리(보고용, 월드 단위)</summary>
            public float HoodShift;
            /// <summary>손 본에만 묶인 덩어리(소맷부리)마다 정점, 이음매 쌍, 크기를 따를 아래팔 부위</summary>
            public readonly (int[] members, (int part, int body)[] seams, BodyRegion forearm)[] CuffParts;
            /// <summary>마지막 배치에서 붙인 소맷부리 수와 팔 방향 배율 최솟값, 둘레 배율 최댓값(보고용)</summary>
            public int CuffAttached;
            public float CuffScale = 1f, CuffGirth;
            /// <summary>끈마다 정점 목록(재배치 반복마다 전체 정점을 훑지 않게 한 번만 만든다)</summary>
            public readonly int[][] StripMembers;

            public FitFrame(FitAnalysis analysis, SkinnedMeshRenderer clone, SkinnedMeshRenderer source, BodyCylinders sourceBody,
                BodyCylinders targetBody, float margin, BoneMatch[] boneMatches, BodyRegion[] boneRegions)
            {
                Analysis = analysis; SourceBody = sourceBody; TargetBody = targetBody; Margin = margin;
                BoneMatches = boneMatches; BoneRegions = boneRegions;
                Vertices = clone.sharedMesh.vertices;
                Weights = source.sharedMesh.boneWeights;
                CloneMatrices = PenetrationFixer.VertexMatrices(clone, clone.sharedMesh);
                Matrix4x4?[] sourceMatrices = PenetrationFixer.VertexMatrices(source, source.sharedMesh);
                Vector3[] sourceVertices = source.sharedMesh.vertices;
                SourceWorld = sourceVertices.Select((v, i) => sourceMatrices[i].HasValue ? sourceMatrices[i].Value.MultiplyPoint3x4(v) : Vector3.positiveInfinity).ToArray();
                SourceRoot = SourceWorld.Select(p => float.IsInfinity(p.x) ? p : FitPlacement.ToRootFrame(analysis.Source.Root, p)).ToArray();
                Mesh mesh = clone.sharedMesh;
                Triangles = Enumerable.Range(0, mesh.subMeshCount).Where(s => mesh.GetTopology(s) == MeshTopology.Triangles).SelectMany(mesh.GetTriangles).ToArray();
                StripOf = StripChains.Find(analysis, source.bones, Weights, SourceWorld, sourceBody.MedianRadius, out StripAnchors, out StripRegions);
                BoneMatch[] matches = BoneMatches;
                bool IsHeadBone(int bone) => bone < matches.Length && matches[bone] != null
                    && (matches[bone].HumanBone == HumanBodyBones.Head || matches[bone].HumanBone == HumanBodyBones.Neck);
                int[] hood = PartAttach.Members(Weights.Take(Vertices.Length).ToArray(), IsHeadBone);
                int[] triangles = Triangles;
                HoodParts = PartAttach.Parts(hood, triangles).Select(part => (part, PartAttach.Seams(part, triangles))).ToArray();
                CuffParts = new[] { (HumanBodyBones.LeftHand, BodyRegion.LeftLowerArm), (HumanBodyBones.RightHand, BodyRegion.RightLowerArm) }
                    .SelectMany(side =>
                    {
                        // 장갑(손가락 본이 있는 손 본)·손가락 대체 연결은 소맷부리가 아니다(OutfitFitter.IsCuffHolder).
                        bool IsHandBone(int bone) => bone < matches.Length && matches[bone] != null && matches[bone].HumanBone == side.Item1
                            && OutfitFitter.IsCuffHolder(analysis, matches[bone]);
                        int[] cuff = PartAttach.Members(Weights.Take(Vertices.Length).ToArray(), IsHandBone);
                        return PartAttach.Parts(cuff, triangles).Select(part => (part, PartAttach.Seams(part, triangles), side.Item2));
                    }).ToArray();
                int[] stripOf = StripOf;
                StripMembers = StripAnchors.Select((_, s) => Enumerable.Range(0, stripOf.Length).Where(i => stripOf[i] == s).ToArray()).ToArray();
            }
        }

        /// <summary>
        /// 배치한 뒤 형태를 검사해, 무너진 부위만 눌림 상한을 낮춰(옷을 더 길게) 다시 배치한다(PRD 0장 형태 보존 최우선).
        /// 더 낮출 수 없거나 무너진 부위가 없으면 멈춘다. 마지막 배치의 이동량(메시 공간)을 돌려준다.
        /// </summary>
        static Vector3[] PlaceKeepingForm(FitFrame frame, ref SquashCaps caps, out bool[] isFullyPlaced, out int moved, out bool[] isStrip)
        {
            BodyRegion[] mainRegion = TriangleRegions(frame);
            Vector3[] deltas = Place(frame, caps, out isFullyPlaced, out moved, out Vector3[] placed, out isStrip);
            for (int attempt = 0; attempt < MaxFormRetries; attempt++)
            {
                BodyRegion[] measured = FullyPlacedRegions(frame.Triangles, mainRegion, isFullyPlaced, frame.StripOf);
                List<BodyRegion> collapsed = FormCheck.Collapsed(FormCheck.CollapsedShares(frame.SourceWorld, placed, frame.Triangles, measured));
                Dictionary<BodyRegion, float> changes = EffectiveLowering(frame, caps, collapsed);
                if (changes.Count == 0) break;
                caps = caps.With(changes);
                deltas = Place(frame, caps, out isFullyPlaced, out moved, out placed, out isStrip);
            }
            return deltas;
        }

        /// <summary>
        /// 무너진 부위마다 실제 늘임 배율이 커지는 데까지 상한을 낮춘 값. 하한까지 낮춰도 늘임이 그대로인 부위
        /// (몸 기준 한도에 막혔거나 한쪽으로 눌리지 않은 부위)는 빼서, 효과 없는 재배치와 사실과 다른 보고를 막는다.
        /// </summary>
        static Dictionary<BodyRegion, float> EffectiveLowering(FitFrame frame, SquashCaps caps, IEnumerable<BodyRegion> collapsed)
        {
            const float MinStretchGain = 1e-3f;
            var changes = new Dictionary<BodyRegion, float>();
            foreach (BodyRegion region in collapsed)
            {
                float cap = caps.Of(region);
                float stretch = BodyCylinders.RegionStretch(frame.SourceBody, frame.TargetBody, region, cap);
                while (cap > SquashCaps.MinCap)
                {
                    cap = SquashCaps.Step(cap);
                    bool isLonger = BodyCylinders.RegionStretch(frame.SourceBody, frame.TargetBody, region, cap) > stretch + MinStretchGain;
                    if (!isLonger) continue;
                    changes[region] = cap;
                    break;
                }
            }
            return changes;
        }

        /// <summary>삼각형마다 부위: 세 정점의 주 본 부위가 같은 삼각형만 그 부위로, 부위가 섞인 면(소맷부리·아랫단 경계)은 None.</summary>
        static BodyRegion[] TriangleRegions(FitFrame frame)
        {
            BodyRegion MainRegion(int v)
            {
                bool isKnown = v < frame.Weights.Length && frame.Weights[v].boneIndex0 < frame.BoneRegions.Length;
                return isKnown ? frame.BoneRegions[frame.Weights[v].boneIndex0] : BodyRegion.None;
            }
            var result = new BodyRegion[frame.Triangles.Length / 3];
            for (int t = 0; t < result.Length; t++)
            {
                BodyRegion a = MainRegion(frame.Triangles[t * 3]);
                bool isSameRegion = a == MainRegion(frame.Triangles[t * 3 + 1]) && a == MainRegion(frame.Triangles[t * 3 + 2]);
                result[t] = isSameRegion ? a : BodyRegion.None;
            }
            return result;
        }

        /// <summary>
        /// 세 정점이 모두 부위 규칙으로 옮겨진 삼각형만 부위를 남긴다. 일부만 옮긴 면은 서로 다른 방식이 섞여 눌림이 부풀려지고,
        /// 끈 면(통째로 배치)은 눌림이 없어 부위의 무너진 비율을 희석하므로 뺀다.
        /// </summary>
        static BodyRegion[] FullyPlacedRegions(int[] triangles, BodyRegion[] mainRegion, bool[] isFullyPlaced, int[] stripOf)
        {
            bool IsBodyRuled(int v) => isFullyPlaced[v] && stripOf[v] < 0;
            var result = new BodyRegion[mainRegion.Length];
            for (int t = 0; t < result.Length; t++)
            {
                bool isAllPlaced = IsBodyRuled(triangles[t * 3]) && IsBodyRuled(triangles[t * 3 + 1]) && IsBodyRuled(triangles[t * 3 + 2]);
                result[t] = isAllPlaced ? mainRegion[t] : BodyRegion.None;
            }
            return result;
        }

        /// <summary>주어진 눌림 상한으로 배치한다. 이동량(메시 공간)과 배치된 월드 위치(옮기지 않은 정점은 지금 위치)를 돌려준다.</summary>
        static Vector3[] Place(FitFrame frame, SquashCaps caps, out bool[] isFullyPlaced, out int moved, out Vector3[] placedWorld, out bool[] isStrip)
        {
            Transform targetRoot = frame.Analysis.Target.Root;
            Vector3[] boneShifts = frame.BoneMatches.Select(m => CuffShift(frame.Analysis, m, frame.SourceBody, frame.TargetBody, targetRoot, caps)).ToArray();
            var placement = new Placement(frame.SourceBody, frame.TargetBody, targetRoot, frame.Margin, caps, frame.BoneRegions, boneShifts);
            int n = frame.Vertices.Length;
            var deltas = new Vector3[n];
            var current = new Vector3[n];
            var isUsable = new bool[n];
            isFullyPlaced = new bool[n];
            placedWorld = new Vector3[n];
            moved = 0;
            for (int i = 0; i < n; i++)
            {
                Matrix4x4? m = frame.CloneMatrices[i];
                isUsable[i] = m.HasValue && i < frame.Weights.Length && !float.IsInfinity(frame.SourceRoot[i].x) && Mathf.Abs(m.Value.determinant) >= MinDeterminant;
                current[i] = m.HasValue ? m.Value.MultiplyPoint3x4(frame.Vertices[i]) : Vector3.positiveInfinity;
                placedWorld[i] = isUsable[i] ? Blend(frame.Weights[i], placement, frame.SourceRoot[i], current[i], out isFullyPlaced[i]) : current[i];
            }
            isStrip = KeepStripShapes(frame, caps, isUsable, isFullyPlaced, placedWorld);
            AttachHood(frame, isUsable, isFullyPlaced, isStrip, placedWorld);
            AttachCuffs(frame, caps, isUsable, isFullyPlaced, isStrip, placedWorld);
            for (int i = 0; i < n; i++)
            {
                if (!isUsable[i]) continue;
                Vector3 delta = placedWorld[i] - current[i];
                if (delta.sqrMagnitude < MinMove * MinMove) continue;
                deltas[i] = frame.CloneMatrices[i].Value.inverse.MultiplyVector(delta);
                moved++;
            }
            return deltas;
        }

        /// <summary>
        /// 끈 정점을 매달린 지점(이미 부위 규칙으로 배치됨)에서 원본 모양 그대로, 부위의 길이·굵기 배율 기하평균만큼 균등하게 둔다.
        /// 부위 규칙으로 두면 굵기와 길이가 따로 맞춰져 끈이 한쪽으로 눌린다(inuinu 끈 눌림 중앙값 2.02 -> 1.00).
        /// 방향은 아바타 루트 기준으로 원본을 따르되, 몸이 원본보다 앞으로 둥근 아바타(inuinu 배)는 끈이 몸 안으로 들어가
        /// 뒤따르는 밀어내기에서 구겨지므로 매달린 지점을 중심으로 몸 바깥쪽으로 필요한 만큼만 통째로 돌린다(정점마다 밀면 끈이 눌림).
        /// 옮긴 끈 정점은 다 옮긴 정점으로 표시해 뒤따르는 관통 보정이 정점마다 밀지 않게 한다(형태 검사 비율에서는 따로 뺀다).
        /// 실제로 통째 배치한 끈 정점(매달린 지점 포함) 표시를 돌려준다.
        /// </summary>
        static bool[] KeepStripShapes(FitFrame frame, SquashCaps caps, bool[] isUsable, bool[] isFullyPlaced, Vector3[] placedWorld)
        {
            Transform targetRoot = frame.Analysis.Target.Root;
            var isStrip = new bool[placedWorld.Length];
            for (int strip = 0; strip < frame.StripAnchors.Length; strip++)
            {
                int anchor = frame.StripAnchors[strip];
                if (!isUsable[anchor]) continue;
                BodyRegion region = frame.StripRegions[strip];
                float scale = BodyCylinders.UniformScale(frame.SourceBody, frame.TargetBody, region, caps.Of(region));
                Vector3 pivot = FitPlacement.ToRootFrame(targetRoot, placedWorld[anchor]);
                List<int> members = frame.StripMembers[strip].Where(i => isUsable[i] && i != anchor).ToList();
                isStrip[anchor] = true;
                Vector3[] rigid = members.Select(i => pivot + (frame.SourceRoot[i] - frame.SourceRoot[anchor]) * scale).ToArray();
                Quaternion swing = ClearingSwing(frame.TargetBody, region, pivot, rigid, frame.Margin);
                for (int k = 0; k < members.Count; k++)
                {
                    placedWorld[members[k]] = FitPlacement.FromRootFrame(targetRoot, pivot + swing * (rigid[k] - pivot));
                    isFullyPlaced[members[k]] = true;
                    isStrip[members[k]] = true;
                }
            }
            return isStrip;
        }

        /// <summary>소맷부리 둘레 배율 상한: 팔 방향 배율의 이 배수(손 크기를 잘못 재 소맷부리가 풍선처럼 커지지 않게)</summary>
        const float MaxCuffGirthToLength = 2f;
        /// <summary>소맷부리가 원래 놓인 곳에서 옮겨질 수 있는 평균 거리 상한: 대상 위팔+아래팔 길이 대비 비율(넘으면 옮기지 않음)</summary>
        const float MaxCuffMoveToArm = 1f;

        /// <summary>
        /// 손 본에만 묶인 덩어리(소맷부리)마다 팔 방향은 아래팔 균등 배율, 둘레는 대상 손을 덮는 배율로 맞춰 아래팔 소매 끝에 붙인다.
        /// 손 본을 따르면 원본 크기 그대로 손목 너머에 남아 짧은 팔에서 소매가 팔보다 훨씬 길어졌고(inuinu 팔의 2.25배, 원본 Felis 1.21배),
        /// 둘레까지 아래팔 배율로 줄이면 굵은 앞발이 소매를 뚫었다(관통 259 -> 305). 붙인 정점은 뒤따르는 관통 보정이 계속 민다(후드와 같음).
        /// </summary>
        static void AttachCuffs(FitFrame frame, SquashCaps caps, bool[] isUsable, bool[] isFullyPlaced, bool[] isStrip, Vector3[] placedWorld)
        {
            frame.CuffAttached = 0;
            frame.CuffScale = 1f;
            frame.CuffGirth = 0f;
            foreach (var cuff in frame.CuffParts)
            {
                if (!TryAttachCuff(frame, caps, cuff, isUsable, isFullyPlaced, isStrip, placedWorld, out float along, out float across)) continue;
                frame.CuffAttached++;
                frame.CuffScale = Mathf.Min(frame.CuffScale, along);
                frame.CuffGirth = Mathf.Max(frame.CuffGirth, across);
            }
        }

        /// <summary>
        /// 소맷부리 덩어리 하나를 맞춰 붙인다. 이음매는 해당 아래팔 소매(몸통 규칙으로 다 옮긴, 끈이 아닌 정점)와의 쌍만 쓰고,
        /// 쌍이 모자라거나 원래 자리에서 팔 길이보다 멀리 옮겨지면 그대로 둔다.
        /// </summary>
        static bool TryAttachCuff(FitFrame frame, SquashCaps caps, (int[] members, (int part, int body)[] seams, BodyRegion forearm) cuff,
            bool[] isUsable, bool[] isFullyPlaced, bool[] isStrip, Vector3[] placedWorld, out float along, out float across)
        {
            var (members, seams, forearm) = cuff;
            along = BodyCylinders.UniformScale(frame.SourceBody, frame.TargetBody, forearm, caps.Of(forearm));
            float hand = BodyCylinders.HandScale(frame.SourceBody, frame.TargetBody, forearm == BodyRegion.LeftLowerArm);
            across = Mathf.Clamp(hand, along, along * MaxCuffGirthToLength);
            bool hasAxes = frame.SourceBody.TryAxis(forearm, out Vector3 fromAxis) & frame.TargetBody.TryAxis(forearm, out Vector3 toAxis);
            if (!hasAxes) return false;
            float a = along, c = across;
            Vector3 Stretch(Vector3 offset) => PartAttach.AxisStretch(offset, fromAxis, toAxis, a, c);
            bool IsForearmSleeve(int v) => isFullyPlaced[v] && !isStrip[v] && frame.Weights[v].boneIndex0 < frame.BoneRegions.Length
                && frame.BoneRegions[frame.Weights[v].boneIndex0] == forearm;
            var chosen = seams.Where(p => isUsable[p.part] && isUsable[p.body] && IsForearmSleeve(p.body)).ToList();
            Transform targetRoot = frame.Analysis.Target.Root;
            Vector3[] provisional = (Vector3[])placedWorld.Clone();
            int[] usable = members.Where(v => isUsable[v]).ToArray();
            foreach (int v in usable) provisional[v] = FitPlacement.FromRootFrame(targetRoot, Stretch(frame.SourceRoot[v]));
            if (usable.Length == 0 || !PartAttach.TryShift(chosen, provisional, (part, body) => targetRoot.rotation * Stretch(frame.SourceRoot[body] - frame.SourceRoot[part]), out Vector3 shift)) return false;
            BodyRegion upper = forearm == BodyRegion.LeftLowerArm ? BodyRegion.LeftUpperArm : BodyRegion.RightUpperArm;
            float maxMove = (frame.TargetBody.Length(upper) + frame.TargetBody.Length(forearm)) * MaxCuffMoveToArm;
            float move = usable.Aggregate(Vector3.zero, (sum, v) => sum + provisional[v] + shift - placedWorld[v]).magnitude / usable.Length;
            if (move > maxMove) return false;
            foreach (int v in usable) placedWorld[v] = provisional[v] + shift;
            return true;
        }

        /// <summary>후드 덩어리를 옮길 수 있는 최대 거리: 대상 몸통 반지름(중앙값) 대비 비율. 넘으면 그 길이로 자른다.</summary>
        const float MaxHoodShiftToTorsoRadius = 0.5f;

        /// <summary>
        /// 머리·목 본에만 묶인 덩어리(후드)마다 모양 그대로 통째로 옮겨 몸판 목둘레 이음매의 벌어짐(축별 중앙값)을 없앤다(PartAttach).
        /// 이음매의 몸판 쪽은 몸통 규칙으로 다 옮긴 정점만 쓴다(목 가중치가 섞이거나 끈이면 벌어짐을 잘못 잼). 그런 쌍이 모자라면 전체 쌍을 쓴다.
        /// 이음매가 없는 덩어리(따로 떨어진 목걸이 등)는 옮기지 않는다. 비율 차이로 생기는 이음매 변 길이 차이(수 mm)는 무시한다.
        /// </summary>
        static void AttachHood(FitFrame frame, bool[] isUsable, bool[] isFullyPlaced, bool[] isStrip, Vector3[] placedWorld)
        {
            Quaternion toTarget = frame.Analysis.Target.Root.rotation;
            float maxShift = frame.TargetBody.MedianRadius(BodyRegion.Torso) * MaxHoodShiftToTorsoRadius;
            frame.HoodShift = 0f;
            foreach (var (members, seams) in frame.HoodParts)
            {
                var usable = seams.Where(p => isUsable[p.part] && isUsable[p.body]).ToList();
                var bodyRuled = usable.Where(p => isFullyPlaced[p.body] && !isStrip[p.body]).ToList();
                var chosen = bodyRuled.Count >= PartAttach.MinSeamPairs ? bodyRuled : usable;
                bool hasShift = PartAttach.TryShift(chosen, placedWorld, (part, body) => toTarget * (frame.SourceRoot[body] - frame.SourceRoot[part]), out Vector3 shift);
                if (!hasShift) continue;
                shift = Vector3.ClampMagnitude(shift, maxShift);
                foreach (int v in members)
                    if (isUsable[v]) placedWorld[v] += shift;
                frame.HoodShift = Mathf.Max(frame.HoodShift, shift.magnitude);
            }
        }

        /// <summary>
        /// 끈(루트 기준 점들)을 매달린 지점 중심으로 몸 바깥쪽으로 돌리는 가장 작은 회전: 몸 표면에서 clearance만큼 떨어지지 못한 깊이가
        /// 0이 되는 첫 각도(SwingStep씩 MaxSwing까지), 없으면 가장 덜 들어간 각도.
        /// </summary>
        static Quaternion ClearingSwing(BodyCylinders body, BodyRegion region, Vector3 pivot, Vector3[] points, float clearance)
        {
            const float SwingStep = 3f, MaxSwing = 60f, MinAxis = 1e-6f;
            if (points.Length == 0 || !body.TryOutward(region, pivot, out Vector3 outward)) return Quaternion.identity;
            Vector3 hang = points.OrderByDescending(p => (p - pivot).sqrMagnitude).First() - pivot;
            Vector3 axis = Vector3.Cross(hang, outward);
            if (axis.magnitude < MinAxis) return Quaternion.identity;
            float Depth(Quaternion q) => points.Max(p => body.TryClearance(region, pivot + q * (p - pivot), out float c) ? clearance - c : 0f);
            Quaternion best = Quaternion.identity;
            float bestDepth = Depth(best);
            for (float degrees = SwingStep; degrees <= MaxSwing && bestDepth > 0f; degrees += SwingStep)
            {
                Quaternion q = Quaternion.AngleAxis(degrees, axis.normalized);
                float depth = Depth(q);
                if (depth >= bestDepth) continue;
                best = q;
                bestDepth = depth;
            }
            return best;
        }

        /// <summary>
        /// 스킨 가중치마다 그 본 부위로 옮긴 위치(범위 밖이거나 옮길 수 없으면 지금 위치 + 손 쪽 이동)를 가중 평균한다.
        /// </summary>
        static Vector3 Blend(BoneWeight w, Placement p, Vector3 sourcePoint, Vector3 current, out bool isFullyMapped)
        {
            Vector3 sum = Vector3.zero;
            float total = 0f;
            bool hasUnmapped = false;
            // 정점마다 불려 배열·사전을 만들지 않고 네 가중치를 차례로 더한다.
            void Add(int index, float weight)
            {
                if (weight <= 0f) return;
                bool isKnownBone = index < p.BoneRegions.Length;
                BodyRegion region = isKnownBone ? p.BoneRegions[index] : BodyRegion.None;
                Vector3 rootPoint = Vector3.zero;
                bool isMapped = region != BodyRegion.None && BodyCylinders.TryMap(p.Source, p.Target, region, sourcePoint, p.Margin, p.Caps, out rootPoint);
                hasUnmapped |= !isMapped;
                Vector3 unmapped = current + (isKnownBone ? p.BoneShifts[index] : Vector3.zero);
                sum += (isMapped ? FitPlacement.FromRootFrame(p.TargetRoot, rootPoint) : unmapped) * weight;
                total += weight;
            }
            Add(w.boneIndex0, w.weight0);
            Add(w.boneIndex1, w.weight1);
            Add(w.boneIndex2, w.weight2);
            Add(w.boneIndex3, w.weight3);
            isFullyMapped = total > 0f && !hasUnmapped;
            return total > 0f ? sum / total : current;
        }

        /// <summary>
        /// 원본 의상 본이 따르는 Humanoid 연결: 본 자신이나 가장 가까운 조상 중 Humanoid로 연결된 본의 연결.
        /// 소매·끈처럼 Humanoid가 아닌 의상 전용 본은 부모 Humanoid 본을 따른다. 없으면 null.
        /// </summary>
        static BoneMatch HumanMatchOfOutfitBone(FitAnalysis analysis, Transform bone)
        {
            for (Transform t = bone; t != null && t != analysis.Outfit.parent; t = t.parent)
            {
                BoneMatch match = analysis.Matches.FirstOrDefault(m => m.OutfitBone == t);
                bool isHumanoid = match != null && (match.Kind == MatchKind.Humanoid || match.Kind == MatchKind.HumanoidFallback);
                if (isHumanoid) return match;
            }
            return null;
        }

        /// <summary>
        /// 소맷부리를 다는 손 본(OutfitFitter.IsCuffHolder: 장갑·손가락 대체 연결 제외)을 따르는 옷 본은
        /// 소매 끝이 늘어난 만큼 옮긴다(월드 거리). 그 밖의 본(손목 장식·장갑 등)은 영벡터.
        /// </summary>
        static Vector3 CuffShift(FitAnalysis analysis, BoneMatch match, BodyCylinders source, BodyCylinders target, Transform targetRoot, SquashCaps caps)
        {
            if (match == null || !OutfitFitter.IsCuffHolder(analysis, match)) return Vector3.zero;
            bool isLeft = match.HumanBone == HumanBodyBones.LeftHand;
            return targetRoot.rotation * BodyCylinders.HandShift(source, target, isLeft, caps);
        }
    }
}
