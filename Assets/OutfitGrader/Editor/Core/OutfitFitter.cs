using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>맞추기 결과 요약</summary>
    public sealed class FitResult
    {
        /// <summary>의상을 입은 대상 아바타</summary>
        public GameObject Avatar;
        /// <summary>대상 아바타 안에 새로 들어간 의상 복제본의 최상위 오브젝트</summary>
        public GameObject OutfitRoot;
        /// <summary>이번에 입힌 의상의 메시들(스킨 메시와 본 없는 소품). 본 아래로 옮겨진 것도 포함한다(관통 보정·몸 표면 제외에 사용).</summary>
        public readonly List<Renderer> Meshes = new List<Renderer>();
        /// <summary>복제본 메시 -> 원본 의상의 같은 메시. 표면 간격 보존 맞춤이 원본 정점 위치를 읽는 데 쓴다.</summary>
        public readonly Dictionary<Renderer, Renderer> SourceRenderers = new Dictionary<Renderer, Renderer>();
        public int MovedBones;
        public int FollowingBones;
        public readonly List<string> Lines = new List<string>();

        public string ToReport()
        {
            return $"이동한 본 {MovedBones}개, 부모를 따라간 본 {FollowingBones}개\n" + string.Join("\n", Lines);
        }
    }

    /// <summary>맞추기 선택 사항</summary>
    public sealed class FitOptions
    {
        /// <summary>
        /// 몸 안으로 들어간 의상 정점을 밀어낸 새 메시로 교체하고, 머리 소품은 귀 자리에 구멍을 낸다(에셋 저장은 프리팹 저장 때).
        /// </summary>
        public bool FixPenetration;
        /// <summary>
        /// 관통 보정 중 본에 묶인 상의의 몸통·팔 부위를 원본 몸과의 간격·층 순서를 보존해 다시 배치하고, 남은 겹침을 정리한다
        /// (표면 간격 보존 맞춤, docs/prd/surface-offset-fitting.md). FixPenetration이 꺼져 있으면 쓰지 않는다.
        /// </summary>
        public bool PreserveOffset;
        /// <summary>체형이 많이 다를 때 옷 길이를 정하는 방식(표면 간격 보존을 쓸 때만). 기본은 오버핏(형태 보존 우선).</summary>
        public FitStyle Style = FitStyle.Oversized;
        /// <summary>
        /// 표면 간격 보존 뒤 몸통·소매를 풀어 날카로운 접힘을 펴고, 아랫단은 엉덩이까지만 늘인다(GarmentRelaxer).
        /// 체형 차이가 큰 아바타(치비)용. 아바타당 약 1~2초 더 걸린다. 표면 간격 보존을 쓸 때만.
        /// </summary>
        public bool Relax;
        /// <summary>풀어 주기를 켰을 때 아랫단 늘임 한도(허벅지 길이 대비, 0이면 엉덩이까지)</summary>
        public const float RelaxedHemReachToThigh = 0f;
    }

    /// <summary>대상 아바타 하나에 옷을 맞출 때 공통으로 쓰는 몸 표면·부위 정보</summary>
    sealed class TargetFitContext
    {
        public TargetSurface Surface;
        public List<(Vector3, Vector3)> EarEdges, TailEdges;
        /// <summary>머리 피부 변 격자(후드 두상 맞춤용, 표면 간격 보존을 쓸 때만)</summary>
        public EdgeGrid HeadEdges;
        /// <summary>표면 간격 보존을 쓰지 않으면 null</summary>
        public BodyCylinders TargetBody;
        /// <summary>마무리 정리에 쓰는 대상 몸의 변. 없으면 null.</summary>
        public EdgeGrid BodyEdges;
        /// <summary>표면 간격 보존의 맞춤 방식(부위별 한쪽 눌림 상한)</summary>
        public FitStyle Style;
        /// <summary>배치 뒤 풀어 주기(FitOptions.Relax)와 그로 인해 아랫단을 엉덩이까지로 바꿨는지(허벅지를 잴 수 없으면 false)</summary>
        public bool Relax, HemAtHips;
    }

    /// <summary>
    /// 의상 복제본을 대상 아바타 안에 바로 입힌다(Modular Avatar·키세타처럼 아바타 계층 안에 의상을 두는 관례).
    /// 원본 의상은 바꾸지 않고, 대상 아바타의 변경은 Undo 한 번으로 모두 되돌린다.
    /// </summary>
    public static class OutfitFitter
    {
        public const string UndoName = "Outfit Grader 맞추기";
        /// <summary>모자 판정: 소품 윗면이 정수리보다 이만큼(머리 폭 대비) 아래까지면 정수리에 닿은 것으로 본다.</summary>
        const float CrownReachToHeadWidth = 0.1f;

        public static FitResult Fit(FitAnalysis analysis, FitOptions options = null)
        {
            return FitAll(new[] { analysis }, options)[0];
        }

        /// <summary>여러 의상을 한 번에 입힌다. 전체가 Undo 한 번으로 되돌아간다.</summary>
        public static List<FitResult> FitAll(IReadOnlyList<FitAnalysis> analyses, FitOptions options = null)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                List<FitResult> results = analyses.Select(FitOne).ToList();
                if (options != null && options.FixPenetration) FixPenetrations(analyses, results, options);
                return results;
            }
            finally
            {
                // 중간에 예외가 나도 이미 적용된 부분을 한 번의 Undo로 되돌릴 수 있게 묶는다.
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        /// <summary>
        /// 모든 의상을 입힌 뒤 대상마다 몸 표면·귀·꼬리를 한 번 만들고, 각 의상 메시를 몸 바깥으로 맞춘다.
        /// 몸 표면에서는 이번에 입힌 의상을 뺀다(옷끼리 서로 밀어내지 않게). 마지막에 옷에 가려지는 털 묶음을 숨긴다.
        /// </summary>
        static void FixPenetrations(IReadOnlyList<FitAnalysis> analyses, List<FitResult> results, FitOptions options)
        {
            bool preserveOffset = options.PreserveOffset;
            foreach (var group in analyses.Zip(results, (a, r) => (analysis: a, result: r)).GroupBy(p => p.analysis.Target.Root))
            {
                AvatarRig target = group.First().analysis.Target;
                Transform[] newOutfits = group.SelectMany(p => p.result.Meshes).Where(m => m != null).Select(m => m.transform).ToArray();
                TargetFitContext context = BuildContext(target, newOutfits, preserveOffset);
                group.First().result.Lines.Add(PartsLine(target, context, newOutfits));
                context.Style = options.Style;
                context.Relax = options.Relax && context.TargetBody != null;
                // 아랫단 한도는 이 대상의 모든 의상(같은 원통 객체)에 적용된다. 풀어 주기는 상의 기준 옵션이라 원피스·롱코트도 엉덩이까지가 된다.
                context.HemAtHips = context.Relax && context.TargetBody.SetHemReach(FitOptions.RelaxedHemReachToThigh);
                var garments = new List<Renderer>();
                var placed = new List<(SkinnedMeshRenderer mesh, FitResult result, string name, PlacementReport report)>();
                var sourceBodies = new Dictionary<string, BodyCylinders>();
                foreach (var (analysis, result) in group)
                {
                    BodyCylinders sourceBody = preserveOffset ? SourceBody(analysis, sourceBodies) : null;
                    foreach (Renderer mesh in result.Meshes.Where(m => m != null))
                    {
                        if (!FitMesh(analysis, result, mesh, context, sourceBody, out bool isPlaced, out PlacementReport report)) continue;
                        garments.Add(mesh);
                        if (isPlaced) placed.Add(((SkinnedMeshRenderer)mesh, result, $"{target.Root.name}_{analysis.Outfit.name}_{mesh.name}", report));
                    }
                }
                ResolvePierced(placed, context, target);

                // 털은 옷을 모두 맞춘 뒤의 위치로 판정한다(여러 옷이 함께 덮는 털도 한 번에). 안경 같은 소품은 옷이 아니라 뺀다.
                float torsoRadius = context.TargetBody != null ? context.TargetBody.MedianRadius(BodyRegion.Torso) : 0f;
                foreach (var (renderer, tufts, triangles) in FurHider.Hide(target, garments, torsoRadius))
                    group.First().result.Lines.Add($"털 숨김: {renderer} 털 묶음 {tufts}개, 삼각형 {triangles}개 지움 (옷에 가려지는 털)");
            }
        }

        /// <summary>
        /// 이름 규칙으로 찾는 부위(귀·꼬리·털)를 찾았는지 알리는 보고 한 줄(PRD G3/GA4). 규칙에 맞는 본이 없으면 그 부위 처리(귀·꼬리 구멍, 털 숨김)는
        /// 아무것도 바꾸지 않고 건너뛰므로, 이름이 다른 귀·꼬리·털이 있는 아바타라면 사용자가 알 수 있게 한다.
        /// </summary>
        static string PartsLine(AvatarRig target, TargetFitContext context, Transform[] newOutfits)
        {
            string State(bool isFound) => isFound ? "있음" : "없음(건너뜀)";
            bool hasFur = target.Root.GetComponentsInChildren<Transform>()
                .Any(t => !newOutfits.Any(o => t.IsChildOf(o)) && !target.TryGetHuman(t, out _) && FurHider.FurBoneName.IsMatch(t.name));
            return $"부위 찾기: 귀 {State(context.EarEdges.Count > 0)}, 꼬리 {State(context.TailEdges.Count > 0)}, 털 {State(hasFur)} "
                + "(본 이름 ear·mimi·耳 / tail·tale·shippo·しっぽ·尻尾 / fur·fluff·もふ로 찾음. 다른 이름이면 구멍·털 숨김을 하지 않음)";
        }

        /// <summary>
        /// 원본 몸 원통(원본이 입은 옷은 모두 뺌). 한 번에 옮기는 옷들은 대개 원본·뺄 옷 목록이 같아 한 번만 만들어 나눠 쓴다
        /// (옷마다 다시 만들면 6벌에 약 0.7초).
        /// </summary>
        static BodyCylinders SourceBody(FitAnalysis analysis, Dictionary<string, BodyCylinders> cache)
        {
            List<Transform> excluded = analysis.OtherOutfits.Append(analysis.Outfit).Where(t => t != null).Distinct().ToList();
            string key = analysis.Source.Root.GetInstanceID() + ":" + string.Join(",", excluded.Select(t => t.GetInstanceID()).OrderBy(id => id));
            if (!cache.TryGetValue(key, out BodyCylinders body))
                cache[key] = body = BodyCylinders.Build(BodyPointCloud.Collect(analysis.Source, excluded, includeBodyChildren: true));
            return body;
        }

        static TargetFitContext BuildContext(AvatarRig target, Transform[] newOutfits, bool preserveOffset)
        {
            // 표면 간격 보존을 쓰면 몸 정점을 한 번만 모아(몸 본 포함) 몸 원통에 쓰고, 몸 본을 뺀 것으로 몸 표면을 만든다.
            // 같은 몸 메시를 여러 번 스키닝하지 않게 준비 단계 동안 행렬을 나눠 쓴다(PRD AC8).
            var skins = new SkinCache();
            BodyPointCloud cloud = preserveOffset ? BodyPointCloud.Collect(target, newOutfits, includeBodyChildren: true, skins: skins) : null;
            var context = new TargetFitContext
            {
                Surface = cloud != null ? PenetrationFixer.BuildSurface(target, cloud.WithoutBodyChildren()) : PenetrationFixer.BuildSurface(target, newOutfits),
                EarEdges = AppendageHoleCutter.EarEdges(target, newOutfits, skins),
                TailEdges = AppendageHoleCutter.TailEdges(target, newOutfits, skins),
            };
            if (!preserveOffset) return context;
            context.TargetBody = BodyCylinders.Build(cloud);
            List<(Vector3, Vector3)> bodyEdges = ClothPierceResolver.BodyEdges(target, newOutfits, skins);
            context.BodyEdges = bodyEdges.Count > 0 ? new EdgeGrid(bodyEdges) : null;
            List<(Vector3, Vector3)> headEdges = HoodHeadFitter.HeadEdges(target, newOutfits, skins);
            context.HeadEdges = headEdges.Count > 0 ? new EdgeGrid(headEdges) : null;
            return context;
        }

        /// <summary>표면 간격 보존으로 다시 배치한 옷마다 몸이 뚫은 삼각형을 정리한다(옷을 모두 맞춘 뒤).</summary>
        static void ResolvePierced(List<(SkinnedMeshRenderer mesh, FitResult result, string name, PlacementReport report)> placed, TargetFitContext context,
            AvatarRig target)
        {
            float step = context.BodyEdges != null ? ClothPierceResolver.StepOf(context.TargetBody, RegionMeasurer.HipHeight(target)) : 0f;
            foreach (var (mesh, result, name, report) in placed)
            {
                if (step > 0f)
                {
                    var (before, after) = ClothPierceResolver.Resolve(mesh, context.BodyEdges, target.Root, step, name, report.IsStrip, context.Surface?.Body, context.TargetBody);
                    if (before > 0) result.Lines.Add($"겹침 정리: {mesh.name} 몸이 뚫은 삼각형 {before}개 -> {after}개");
                }
                // 풀어 주기는 겹침 정리까지 마친 뒤에 한다(앞서 하면 뒤따르는 정점별 밀기가 편 접힘을 다시 세움, 실측 관통 193 vs 301).
                int relaxed = report.RelaxRegion != null ? GarmentRelaxer.RelaxMesh(mesh, target.Root, report.RelaxRegion, report.RelaxRestLength, context.TargetBody, context.Surface.Body) : 0;
                string hem = context.HemAtHips ? ", 아랫단은 엉덩이까지" : "";
                if (relaxed > 0) result.Lines.Add($"풀어 주기: {mesh.name} 몸통·소매 정점 {relaxed}개를 {GarmentRelaxer.Steps}단계 풀어 날카로운 접힘을 폄{hem}");
                float drop = 0f;
                int draped = report.RelaxRegion != null && report.NeedsDrape ? Drape(mesh, target, report.RelaxRegion, context, out drop) : 0;
                if (draped > 0) result.Lines.Add($"늘어뜨리기: {mesh.name} 몸통 아래쪽 정점 {draped}개를 중력으로 늘어뜨려 옆구리 겹침을 폄(아랫단 {drop * MillimetersPerMeter:0}mm 내려감)");
                FitHood(mesh, result, name, context, target);
                string remaining = ClothPierceResolver.RemainingByPart(mesh, context.BodyEdges, target);
                if (remaining.Length > 0) result.Lines.Add($"남은 겹침: {mesh.name} 몸이 아직 뚫는 삼각형(부위별) {remaining}");
            }
        }

        /// <summary>후드 두상 맞춤(HoodHeadFitter). 머리 본·머리 피부·머리 폭이 있을 때만.</summary>
        static void FitHood(SkinnedMeshRenderer mesh, FitResult result, string name, TargetFitContext context, AvatarRig target)
        {
            Transform head = target.GetHuman(HumanBodyBones.Head);
            if (head == null || context.HeadEdges == null) return;
            var (shift, before, after) = HoodHeadFitter.Fit(mesh, target, context.HeadEdges, context.BodyEdges, FitPlacement.ToRootFrame(target.Root, head.position),
                context.Surface.HeadShape.Width, name);
            if (shift > 0f)
                result.Lines.Add($"두상 맞춤: {mesh.name} 머리·목 부분을 모양 그대로 머리에서 {shift * MillimetersPerMeter:0}mm 떼어 머리가 뚫는 삼각형 {before}개 -> {after}개");
        }

        /// <summary>풀어 준 상의 몸통 아래쪽을 중력으로 늘어뜨린다(GarmentDraper). 엉덩이·목 본이 없으면 하지 않는다.</summary>
        static int Drape(SkinnedMeshRenderer mesh, AvatarRig target, BodyRegion[] region, TargetFitContext context, out float drop)
        {
            drop = 0f;
            Transform hips = target.GetHuman(HumanBodyBones.Hips), neck = target.GetHuman(HumanBodyBones.Neck);
            if (hips == null || neck == null) return 0;
            float hipY = FitPlacement.ToRootFrame(target.Root, hips.position).y, neckY = FitPlacement.ToRootFrame(target.Root, neck.position).y;
            return GarmentDraper.DrapeMesh(mesh, target.Root, region, context.TargetBody, context.Surface.Body, hipY, neckY, out drop);
        }

        /// <summary>보고 문구의 미터 -> 밀리미터 변환</summary>
        const float MillimetersPerMeter = 1000f;

        /// <summary>
        /// 의상 메시 하나를 몸 바깥으로 맞춘다. 본에 묶인 옷은 관통 정점을 밀어낸 뒤 꼬리가 뚫고 지나가는 자리에,
        /// 정수리를 덮는 소품(모자)은 두상에 맞춰 밀어낸 뒤 귀가 뚫고 지나가는 자리에 구멍을 낸다.
        /// 그 밖의 본 없는 소품(안경 등)은 그대로 둔다. 맞춘(옷·모자) 메시면 true.
        /// 표면 간격 보존을 쓰면 본에 묶인 옷은 먼저 다시 배치하고(isPlaced) 그 뒤 범위 밖 부위(후드 등)를 위해 밀어내기도 한다.
        /// </summary>
        static bool FitMesh(FitAnalysis analysis, FitResult result, Renderer mesh, TargetFitContext context, BodyCylinders sourceBody, out bool isPlaced,
            out PlacementReport report)
        {
            report = null;
            AvatarRig target = analysis.Target;
            TargetSurface surface = context.Surface;
            HeadShape head = surface.HeadShape;
            isPlaced = false;
            bool isRigid = !(mesh is SkinnedMeshRenderer skinned) || FitAnalysis.IsUnskinned(skinned);
            bool isHat = isRigid && target.IsDescendantOf(mesh.transform, HumanBodyBones.Head) && CoversCrown(mesh, head, target.Root);
            if (isRigid && !isHat) return false;

            // 모자는 두상 크기에 맞춰 여유 간격과 밀기 상한을 머리 폭 기준으로 둔다(치비는 엉덩이가 낮아 엉덩이 기준이 너무 작음).
            float hipHeight = RegionMeasurer.HipHeight(target);
            // 옷은 표면 간격 보존을 쓸 때(몸통 원통이 있을 때) 몸통 굵기 기준, 아니면 엉덩이 높이 기준(PRD G1, 끈 결과는 그대로).
            float margin = isHat ? head.Width * PenetrationFixer.MarginToHeadWidth : SurfaceOffsetFitter.MarginOf(context.TargetBody, hipHeight);
            float maxPush = isHat ? head.Width * PenetrationFixer.MaxPushToHeadWidth : SurfaceOffsetFitter.MaxPushOf(context.TargetBody, hipHeight);
            string meshName = $"{target.Root.name}_{analysis.Outfit.name}_{mesh.name}";
            bool[] isKept = null;
            float placeMargin = SurfaceOffsetFitter.MarginOf(context.TargetBody, hipHeight);
            isPlaced = !isRigid && TryPlaceByOffset(analysis, result, (SkinnedMeshRenderer)mesh, context, sourceBody, placeMargin, meshName, out isKept,
                out report);
            // 다시 배치한 옷도 후드·목깃·손 등 범위 밖 본에 걸린 정점은 민다(다시 배치한 정점은 건드리지 않음).
            PenetrationResult pushed = PenetrationFixer.Fix(mesh, isHat ? surface.Head : surface.Body, target, meshName, margin, maxPush, isKept);
            if (pushed.PushedVertices > 0)
                result.Lines.Add($"관통 보정: {mesh.name} 정점 {pushed.PushedVertices}개 밀어냄 (최대 {pushed.MaxPush * 1000f:0.0}mm, 메시는 프리팹 저장 때 에셋으로 저장)");
            string part = isHat ? "귀" : "꼬리";
            // 꼬리 구멍만 꼬리 둘레를 따라 자르거나 가장자리를 꼬리에 붙인다. 모자 귀 구멍은 이미 귀 밑동에 가려지고, 붙이면 귀 옆 모자 가장자리가 거칠어졌다(실측).
            var hug = isHat ? null : new AppendageHoleCutter.HugSettings { Margin = margin, MaxPull = maxPush, Body = surface.Body };
            int removed = AppendageHoleCutter.Cut(mesh, isHat ? context.EarEdges : context.TailEdges, target.Root, meshName, hug);
            if (removed > 0) result.Lines.Add($"{part} 구멍: {mesh.name} 삼각형 {removed}개 지우거나 잘라 냄 ({part}가 뚫고 지나가는 자리)");
            return true;
        }

        /// <summary>표면 간격 보존으로 다시 배치한다. 쓰지 않거나 원본 메시를 못 찾거나 다시 배치한 정점이 없으면 false.</summary>
        static bool TryPlaceByOffset(FitAnalysis analysis, FitResult result, SkinnedMeshRenderer mesh, TargetFitContext context,
            BodyCylinders sourceBody, float margin, string meshName, out bool[] isFullyPlaced, out PlacementReport report)
        {
            isFullyPlaced = null;
            report = null;
            bool canPlace = context.TargetBody != null && sourceBody != null
                && result.SourceRenderers.TryGetValue(mesh, out Renderer original) && original is SkinnedMeshRenderer;
            if (!canPlace) return false;

            var source = (SkinnedMeshRenderer)result.SourceRenderers[mesh];
            int moved = SurfaceOffsetFitter.Fit(analysis, mesh, source, sourceBody, context.TargetBody, margin, context.Style, context.Relax, meshName,
                out isFullyPlaced, out report);
            if (moved > 0) result.Lines.Add($"간격 보존: {mesh.name} 정점 {moved}개를 원본 몸과의 간격대로 다시 배치");
            if (report.Strips > 0) result.Lines.Add($"끈 모양 유지: {mesh.name} 끈 {report.Strips}개를 매달린 지점 기준으로 모양 그대로 크기만 맞춤");
            if (report.HoodShift > 0f) result.Lines.Add($"후드 붙이기: {mesh.name} 머리·목 본에만 묶인 부분을 모양 그대로 {report.HoodShift * 1000f:0}mm 옮겨 목둘레에 붙임");
            if (report.CuffGirth > 0f) result.Lines.Add($"소맷부리 맞춤: {mesh.name} 손 본에만 묶인 부분을 팔 방향 x{report.CuffScale:0.00}(최소), 둘레 x{report.CuffGirth:0.00}(최대, 손을 덮도록)로 맞춰 소매 끝에 붙임");
            if (report.FormNote.Length > 0) result.Lines.Add($"형태 보존: {mesh.name} 형태가 무너지는 부위의 눌림 상한을 낮춰 옷 길이를 남김 ({report.FormNote})");
            return moved > 0;
        }

        /// <summary>위에서 볼 때 정수리를 덮고, 높이가 정수리 근처까지 닿는 소품(모자). 눈높이에 걸친 안경은 해당하지 않는다.</summary>
        static bool CoversCrown(Renderer prop, HeadShape head, Transform root)
        {
            Mesh mesh = FitAnalysis.MeshOf(prop);
            if (mesh == null || head.Width <= 0f) return false;

            Bounds local = mesh.bounds;
            var bounds = new Bounds(FitPlacement.ToRootFrame(root, prop.transform.TransformPoint(local.min)), Vector3.zero);
            for (int i = 1; i < 8; i++)
            {
                var corner = new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
                bounds.Encapsulate(FitPlacement.ToRootFrame(root, prop.transform.TransformPoint(local.min + Vector3.Scale(local.size, corner))));
            }
            bool coversFromAbove = head.Crown.x >= bounds.min.x && head.Crown.x <= bounds.max.x
                && head.Crown.z >= bounds.min.z && head.Crown.z <= bounds.max.z;
            bool reachesCrown = bounds.max.y >= head.Crown.y - head.Width * CrownReachToHeadWidth;
            return coversFromAbove && reachesCrown;
        }

        static FitResult FitOne(FitAnalysis analysis)
        {
            Transform target = analysis.Target.Root;
            Transform sourceRoot = analysis.Source.Root;
            Transform outfit = analysis.Outfit;
            Quaternion rootDelta = target.rotation * Quaternion.Inverse(sourceRoot.rotation);

            // 원본 상태를 먼저 기록해 두고, 이후에는 의상 복제본만 수정한다.
            Dictionary<Transform, BoneState> outfitStates = outfit.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, BoneState.Of);
            Transform outfitClone = CreateOutfitClone(outfit, target, outfitStates[outfit], sourceRoot, rootDelta, analysis.Scale.Body,
                out Dictionary<Transform, Transform> outfitMap);

            var result = new FitResult { Avatar = target.gameObject, OutfitRoot = outfitClone.gameObject };
            result.Meshes.AddRange(outfitClone.GetComponentsInChildren<Renderer>(true).Where(r => FitAnalysis.MeshOf(r) != null));
            foreach (var pair in outfitMap)
            {
                Renderer cloneRenderer = pair.Value.GetComponent<Renderer>(), originalRenderer = pair.Key.GetComponent<Renderer>();
                if (cloneRenderer != null && originalRenderer != null) result.SourceRenderers[cloneRenderer] = originalRenderer;
            }
            // 대상 본 아래로 따로 옮겨질 복제본 본들. 이 본들은 부모의 축별 배율을 물려받지 않으므로 보조 오브젝트로 옮기지 않는다.
            var movingClones = new HashSet<Transform>(analysis.Matches.Where(m => m.IsConnected).Select(m => Lookup(outfitMap, m.OutfitBone)).Where(t => t != null));
            foreach (BoneMatch match in analysis.Matches)
            {
                Transform cloneBone = Lookup(outfitMap, match.OutfitBone);
                if (cloneBone == null) continue;
                if (!match.IsConnected) { result.FollowingBones++; continue; }

                // 직접 지정 등으로 대상 아바타 밖의 본이 들어오면 건너뛴다.
                bool isInsideTarget = match.TargetBone != null && match.TargetBone.IsChildOf(target);
                if (!isInsideTarget) continue;

                result.Lines.Add(MoveBone(analysis, match, cloneBone, outfitStates[match.OutfitBone], rootDelta, movingClones));
                result.MovedBones++;
            }
            return result;
        }

        /// <summary>의상을 복제해 대상 아바타 안에 넣고, 원본 의상 -> 복제본 대응표를 함께 돌려준다.</summary>
        static Transform CreateOutfitClone(Transform outfit, Transform target, BoneState rootState, Transform sourceRoot,
            Quaternion rootDelta, float bodyRatio, out Dictionary<Transform, Transform> outfitMap)
        {
            Transform clone = Object.Instantiate(outfit.gameObject).transform;
            clone.name = outfit.name;
            // 본을 옮기기 전에 대응을 계층 순서로 구해 둔다. 경로 문자열은 같은 이름 형제(좌우 귀걸이 등)를 구분하지 못한다.
            outfitMap = MapClone(outfit, clone);

            clone.SetParent(target, true);
            BoneState placed = FitPlacement.Place(rootState, sourceRoot.position, target.position, rootDelta, bodyRatio);
            clone.SetPositionAndRotation(placed.Position, placed.Rotation);
            Undo.RegisterCreatedObjectUndo(clone.gameObject, UndoName);
            return clone;
        }

        /// <summary>의상 본 하나를 대상 본 아래로 옮기고 위치·회전·크기를 맞춘다. 보고서 한 줄을 돌려준다.</summary>
        static string MoveBone(FitAnalysis analysis, BoneMatch match, Transform cloneBone, BoneState outfitState, Quaternion rootDelta,
            ISet<Transform> movingClones)
        {
            Transform targetBone = match.TargetBone;
            float ratio = analysis.Scale.RatioFor(analysis.Target, targetBone);
            ResolveAnchors(analysis, match, analysis.Source.Root, analysis.Target.Root, targetBone,
                out Vector3? sourceAnchor, out Vector3 targetAnchor, out bool usedBox);
            BoneState placed = FitPlacement.Place(outfitState, sourceAnchor, targetAnchor, rootDelta, ratio);

            // 의상 복제본 밖(대상 본 아래)으로 옮기는 부모 변경은 Undo에 기록해야
            // 되돌릴 때 복제본 안으로 돌아온 뒤 복제본과 함께 삭제된다.
            Undo.SetTransformParent(cloneBone, targetBone, UndoName);
            cloneBone.SetPositionAndRotation(placed.Position, placed.Rotation);
            bool usedSegment = TrySegmentScale(analysis, match, outfitState, out Vector3 lossy, out SegmentRatio segment);
            cloneBone.localScale = FitPlacement.LocalScaleFor(usedSegment ? lossy : placed.LossyScale, targetBone.lossyScale);
            if (usedSegment) KeepChildrenUniform(cloneBone, targetBone, outfitState.LossyScale * segment.Thickness, movingClones);
            // 대상 본과 이름이 겹치지 않게 의상 본에만 의상 이름을 붙인다(소품 메시는 이름 유지).
            bool isBone = cloneBone.GetComponent<Renderer>() == null;
            if (isBone) cloneBone.name = $"{match.OutfitBone.name}_{analysis.Outfit.name}";

            string segmentNote = IsCuffHolder(analysis, match)
                ? $"균등 x{segment.Thickness:0.00}, 아래팔 굵기"
                : $"굵기 x{segment.Thickness:0.00}, 길이 x{segment.Length:0.00}";
            string method = usedBox ? " [구역 상대 위치]" : usedSegment ? $" [{segmentNote}]" : string.Empty;
            return $"{match.OutfitBone.name} -> {targetBone.name} (x{ratio:0.00}){method} {match.Note}".TrimEnd();
        }

        /// <summary>
        /// 소맷부리가 매달리는 손 본은 아래팔 굵기 배율로 균등하게 키운다. 손은 마디 측정 대상이 아니라 몸 비율(inuinu x0.22)을 받아,
        /// 아래팔 소매(x0.73)보다 소맷부리만 작게 오그라들었다. 손 자체는 길이 방향이 없어 균등 배율로 둔다.
        /// </summary>
        static readonly Dictionary<HumanBodyBones, HumanBodyBones> CuffSegments = new Dictionary<HumanBodyBones, HumanBodyBones>
        {
            { HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm },
            { HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm },
        };

        /// <summary>
        /// 팔·몸통 마디에 연결된 의상 본은 길이 방향과 단면 방향을 따로 맞춘다.
        /// 몸 비율 하나로만 줄이면 굵기 비율이 다른 아바타에서 옷이 몸 안으로 묻히기 때문.
        /// </summary>
        /// <remarks>
        /// 길이 축은 원본 자세에서 판정한다(원본 의상 본 회전 + 원본 마디 축). 대상 축을 쓰면
        /// 원본이 A 자세·대상이 T 자세처럼 팔 각도가 크게 다를 때 엉뚱한 축이 길이 축으로 뽑힌다.
        /// </remarks>
        /// <summary>
        /// 소맷부리를 다는 손 본: 손 본에 직접 연결(대체 연결 제외)됐고 아래에 손가락 본이 없는 의상 본.
        /// 장갑처럼 손가락까지 덮는 의상은 손가락이 몸 비율로 남아 손바닥만 커지므로 빼고,
        /// 대상에 손가락이 없어 손가락 의상 본이 손 본에 대체 연결된 경우도 뺀다.
        /// </summary>
        internal static bool IsCuffHolder(FitAnalysis analysis, BoneMatch match)
        {
            bool isDirectHand = match.Kind == MatchKind.Humanoid && !match.SourcePoint.HasValue && CuffSegments.ContainsKey(match.HumanBone);
            if (!isDirectHand) return false;
            bool hasFingers = analysis.Matches.Any(m => m != match && m.OutfitBone != null
                && m.OutfitBone.IsChildOf(match.OutfitBone) && IsFinger(m.HumanBone));
            return !hasFingers;
        }

        static bool IsFinger(HumanBodyBones bone) => bone >= HumanBodyBones.LeftThumbProximal && bone <= HumanBodyBones.RightLittleDistal;

        static bool TrySegmentScale(FitAnalysis analysis, BoneMatch match, BoneState outfitState, out Vector3 lossy, out SegmentRatio segment)
        {
            lossy = outfitState.LossyScale;
            segment = default;
            bool isSkeletalMatch = !match.SourcePoint.HasValue
                && (match.Kind == MatchKind.Humanoid || match.Kind == MatchKind.HumanoidFallback);
            bool isCuffHolder = IsCuffHolder(analysis, match) && CuffSegments.TryGetValue(match.HumanBone, out HumanBodyBones forearm)
                && analysis.Scale.TryGetSegment(forearm, out segment);
            if (isCuffHolder)
            {
                lossy = outfitState.LossyScale * segment.Thickness;
                return true;
            }
            if (!isSkeletalMatch || !analysis.Scale.TryGetSegment(match.HumanBone, out segment)) return false;
            if (!BodySegmentMeasurer.TryGetAxis(analysis.Source, match.HumanBone, out Vector3 sourceAxis)) return false;

            int lengthAxis = FitPlacement.DominantAxis(outfitState.Rotation, sourceAxis);
            lossy = FitPlacement.AxisScale(outfitState.LossyScale, lengthAxis, segment.Length, segment.Thickness);
            return true;
        }

        /// <summary>
        /// 축마다 다른 배율을 받은 본 아래에 남는 자식(치마·리본처럼 따라가는 본, 붙어 있던 메시)을
        /// 같은 자리의 균등 배율 보조 오브젝트 아래로 옮긴다. 비균등 부모 아래 자식이 회전하면 형태가 비틀리기 때문.
        /// </summary>
        static void KeepChildrenUniform(Transform cloneBone, Transform targetBone, Vector3 uniformLossy, ISet<Transform> movingClones)
        {
            Transform[] stayingChildren = cloneBone.Cast<Transform>().Where(c => !movingClones.Contains(c)).ToArray();
            if (stayingChildren.Length == 0) return;

            var holder = new GameObject($"{cloneBone.name}_자식").transform;
            Undo.RegisterCreatedObjectUndo(holder.gameObject, UndoName);
            holder.SetParent(targetBone, false);
            holder.SetPositionAndRotation(cloneBone.position, cloneBone.rotation);
            float uniform = (uniformLossy.x + uniformLossy.y + uniformLossy.z) / 3f;
            holder.localScale = FitPlacement.LocalScaleFor(Vector3.one * uniform, targetBone.lossyScale);

            foreach (Transform child in stayingChildren)
            {
                Vector3 localPosition = child.localPosition;
                Quaternion localRotation = child.localRotation;
                Vector3 localScale = child.localScale;
                Undo.SetTransformParent(child, holder, UndoName);
                child.localPosition = localPosition;
                child.localRotation = localRotation;
                child.localScale = localScale;
            }
        }

        /// <summary>
        /// 위치 기준점을 정한다. 소품이 머리 구역에 붙으면 메시 중심을 구역 상자 안 같은 상대 위치로 옮기고,
        /// 그 밖에는 원본 본 -> 대상 본 피벗을 기준으로 삼는다.
        /// </summary>
        static void ResolveAnchors(FitAnalysis analysis, BoneMatch match, Transform sourceRoot, Transform targetRoot,
            Transform targetBone, out Vector3? sourceAnchor, out Vector3 targetAnchor, out bool usedBox)
        {
            RegionBox box = analysis.Scale.BoxFor(analysis.Target, match.TargetBone);
            usedBox = match.SourcePoint.HasValue && box.Valid;
            if (usedBox)
            {
                Vector3 inSource = FitPlacement.ToRootFrame(sourceRoot, match.SourcePoint.Value);
                Vector3 inTarget = FitPlacement.MapInBox(inSource, box.Source, box.Target);
                // 얼굴 앞면 간격 보정은 상자 앞쪽 절반의 소품(안경·마스크 등)에만 쓴다. 뒤통수 리본에 쓰면 머리 안으로 들어간다.
                bool isFrontProp = inSource.z >= box.Source.center.z;
                if (isFrontProp) inTarget.z = SurfaceDepth(box, inSource, inTarget, analysis.Scale.RatioFor(analysis.Target, match.TargetBone));
                sourceAnchor = match.SourcePoint.Value;
                targetAnchor = FitPlacement.FromRootFrame(targetRoot, inTarget);
                return;
            }

            sourceAnchor = match.SourceAnchor != null ? match.SourceAnchor.position : (Vector3?)null;
            targetAnchor = targetBone.position;
        }

        /// <summary>표면 탐색 반경: 구역 폭의 이 비율 (얼굴이면 눈 주변 정도)</summary>
        const float SurfaceRadiusRatio = 0.2f;

        /// <summary>소품과 얼굴 앞면 사이 간격을 대상 얼굴 앞면 기준으로 유지한 깊이. 표면을 못 찾으면 상자 비율 결과 유지.</summary>
        static float SurfaceDepth(RegionBox box, Vector3 inSource, Vector3 inTarget, float ratio)
        {
            float? sourceSurface = FitPlacement.FrontDepth(box.SourcePoints, inSource, box.Source.size.x * SurfaceRadiusRatio);
            float? targetSurface = FitPlacement.FrontDepth(box.TargetPoints, inTarget, box.Target.size.x * SurfaceRadiusRatio);
            bool hasSurfaces = sourceSurface.HasValue && targetSurface.HasValue;
            return hasSurfaces ? FitPlacement.KeepSurfaceGap(inSource.z, sourceSurface.Value, targetSurface.Value, ratio) : inTarget.z;
        }

        /// <summary>Instantiate 직후 원본과 복제본은 계층 순서가 같으므로 순서대로 짝짓는다.</summary>
        public static Dictionary<Transform, Transform> MapClone(Transform originalRoot, Transform cloneRoot)
        {
            Transform[] originals = originalRoot.GetComponentsInChildren<Transform>(true);
            Transform[] clones = cloneRoot.GetComponentsInChildren<Transform>(true);
            var map = new Dictionary<Transform, Transform>(originals.Length);
            for (int i = 0; i < originals.Length && i < clones.Length; i++) map[originals[i]] = clones[i];
            return map;
        }

        static Transform Lookup(Dictionary<Transform, Transform> map, Transform original)
        {
            return original != null && map.TryGetValue(original, out Transform clone) ? clone : null;
        }
    }
}
