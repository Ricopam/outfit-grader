using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 몸 마디 하나를 원통으로 보고, (축 방향 길이 위치 0~1, 둘레 각도) 칸마다 몸의 바깥 반지름을 담는 표.
    /// 옷을 옮길 때 "그 위치의 원본 몸 표면"과 "같은 위치의 대상 몸 표면"을 찾기 위함.
    /// </summary>
    public sealed class CylinderTable
    {
        public const int LengthBins = 16;
        public const int AngleBins = 24;
        /// <summary>
        /// 움푹한 칸 메우기 횟수. 정점이 성긴 곳(가는 팔, 저폴리 마스코트)은 칸에 바깥 정점이 안 잡혀 반지름이 움푹 꺼지고,
        /// 옷이 그 칸만 안으로 끌려 뾰족하게 찢겨 보인다. 볼록한 곳(가슴 등)은 깎으면 옷이 몸 안으로 들어가므로 그대로 둔다.
        /// </summary>
        public const int SmoothPasses = 3;

        readonly float[,] radius = new float[LengthBins, AngleBins];
        readonly bool[,] filled = new bool[LengthBins, AngleBins];
        float medianRadius;

        public bool HasData { get; private set; }

        /// <summary>표본(길이 위치, 각도 라디안, 반지름)으로 칸마다 가장 바깥 반지름을 담고 빈 칸을 이웃 값으로 채운다.</summary>
        /// <param name="axisLength">마디 축 길이(월드 단위). 빈 칸을 길이 방향 이웃으로도 채울 때 줄 간격을 재는 데 쓴다. 0이면 같은 줄(없으면 가까운 줄 복사)로만 채운다.</param>
        public static CylinderTable Build(IEnumerable<(float t, float angle, float radius)> samples, float axisLength = 0f)
        {
            var table = new CylinderTable();
            foreach (var (t, angle, r) in samples)
            {
                int i = LengthIndex(t), j = AngleIndex(angle);
                if (table.filled[i, j] && table.radius[i, j] >= r) continue;
                table.radius[i, j] = r;
                table.filled[i, j] = true;
                table.HasData = true;
            }
            if (!table.HasData) return table;
            table.FillGaps(axisLength / LengthBins);
            for (int pass = 0; pass < SmoothPasses; pass++) table.FillLengthDips();
            for (int pass = 0; pass < SmoothPasses; pass++) table.Smooth();
            table.medianRadius = table.radius.Cast<float>().OrderBy(r => r).ElementAt(table.radius.Length / 2);
            return table;
        }

        /// <summary>모든 칸 반지름의 중앙값(마디 굵기의 대표값, 배·가슴 돌출에 덜 흔들림). 값이 없으면 0. 만들 때 한 번 계산한다.</summary>
        public float MedianRadius() => medianRadius;

        /// <summary>
        /// (t, 각도)의 몸 바깥 반지름. 칸 중심 사이를 길이·둘레 방향으로 선형 보간한다.
        /// t는 0~1로 자른다(마디 끝 너머는 끝 단면을 쓴다).
        /// </summary>
        public float Sample(float t, float angle)
        {
            float ft = Mathf.Clamp01(t) * LengthBins - 0.5f;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(ft), 0, LengthBins - 1), i1 = Mathf.Min(i0 + 1, LengthBins - 1);
            float wt = Mathf.Clamp01(ft - i0);
            float fa = Wrap(angle) / (2f * Mathf.PI) * AngleBins - 0.5f;
            int j0 = ((Mathf.FloorToInt(fa) % AngleBins) + AngleBins) % AngleBins, j1 = (j0 + 1) % AngleBins;
            float wa = fa - Mathf.Floor(fa);
            float near = Mathf.Lerp(radius[i0, j0], radius[i0, j1], wa);
            float far = Mathf.Lerp(radius[i1, j0], radius[i1, j1], wa);
            return Mathf.Lerp(near, far, wt);
        }

        /// <summary>
        /// 옷 정점의 축에서 거리(rho)를 원본 몸 표면 반지름 기준에서 대상 몸 기준으로 옮긴다.
        /// 몸 바깥(rho >= 원본 표면)은 대상 표면 + 여유 간격 + 원본 간격 x 굵기 비율, 몸 안쪽은 같은 비율로 줄인다.
        /// 단조 증가라 안쪽 층(셔츠)이 바깥 층(후드티)을 넘지 않고, 몸 바깥 옷은 대상 표면에서 여유 간격 이상 떨어진다.
        /// </summary>
        public static float MapRadius(float rho, float sourceSurface, float targetSurface, float margin)
        {
            float thickness = targetSurface / sourceSurface;
            float outerStart = targetSurface + margin;
            bool isOutsideBody = rho >= sourceSurface;
            return isOutsideBody ? outerStart + (rho - sourceSurface) * thickness : rho * (outerStart / sourceSurface);
        }

        /// <summary>
        /// 길이 방향으로 위아래 두 줄보다 모두 낮은(한 줄만 꺼진) 칸을 두 줄 평균으로 올린다.
        /// 둥근 몸의 옆·뒤 경계처럼 정점이 성긴 곳은 줄마다 먼 정점이 잡히거나 안 잡혀 반지름이 줄마다 오르내리고(inuinu 127도 칸 163~192mm),
        /// 옷이 그대로 따라 옆선이 톱니가 됐다. 허리처럼 여러 줄에 걸친 실제 오목함과 볼록한 칸은 건드리지 않는다.
        /// </summary>
        void FillLengthDips()
        {
            var next = (float[,])radius.Clone();
            for (int i = 1; i < LengthBins - 1; i++)
            for (int j = 0; j < AngleBins; j++)
            {
                float below = radius[i - 1, j], above = radius[i + 1, j];
                bool isDip = radius[i, j] < below && radius[i, j] < above;
                if (isDip) next[i, j] = (below + above) * 0.5f;
            }
            System.Array.Copy(next, radius, next.Length);
        }

        /// <summary>
        /// 칸마다 자신과 둘레·길이 방향 이웃 4칸의 평균이 자기 값보다 크면 평균으로 올린다(움푹한 칸만 메움).
        /// 둘레는 이어지고, 길이 끝은 자기 값을 다시 쓴다.
        /// </summary>
        void Smooth()
        {
            var next = new float[LengthBins, AngleBins];
            for (int i = 0; i < LengthBins; i++)
            for (int j = 0; j < AngleBins; j++)
            {
                int before = Mathf.Max(i - 1, 0), after = Mathf.Min(i + 1, LengthBins - 1);
                float around = radius[i, (j + AngleBins - 1) % AngleBins] + radius[i, (j + 1) % AngleBins];
                float average = (radius[i, j] + around + radius[before, j] + radius[after, j]) / 5f;
                next[i, j] = Mathf.Max(radius[i, j], average);
            }
            System.Array.Copy(next, radius, next.Length);
        }

        /// <summary>
        /// 빈 칸: 같은 줄에서 둘레로 가장 가까운 칸(호 길이)과 같은 각도의 위·아래 줄(줄 간격 x 줄 차이) 중 표면 거리가 가까운 쪽으로 채운다.
        /// 위·아래가 더 가까우면 두 줄 사이를 보간한다. 둘 다 없으면 가장 가까운 줄을 복사한다. rowStep이 0이면 길이 방향은 쓰지 않는다.
        /// 저폴리 몸은 한 줄에 정점이 몇 칸만 잡혀, 둘레로만 채우면 배 앞 빈 칸이 뒤쪽 옆구리 반지름(inuinu 192mm, 실제 배 84mm)이 되어
        /// 옷 앞섶이 가시처럼 앞으로 밀려났다.
        /// </summary>
        void FillGaps(float rowStep)
        {
            var known = (bool[,])filled.Clone();
            var rowHasData = new bool[LengthBins];
            for (int i = 0; i < LengthBins; i++)
                for (int j = 0; j < AngleBins; j++) rowHasData[i] |= known[i, j];
            var unresolved = new List<(int i, int j)>();
            for (int i = 0; i < LengthBins; i++)
            for (int j = 0; j < AngleBins; j++)
            {
                if (known[i, j]) continue;
                bool hasRow = TryFromRow(known, i, j, out float rowValue, out float rowDistance);
                float columnValue = 0f, columnDistance = float.MaxValue;
                bool hasColumn = rowStep > 0f && TryFromColumn(known, i, j, rowStep, out columnValue, out columnDistance);
                bool isColumnNearer = hasColumn && (!hasRow || columnDistance < rowDistance);
                if (isColumnNearer) radius[i, j] = columnValue;
                else if (hasRow) radius[i, j] = rowValue;
                else unresolved.Add((i, j));
            }
            foreach (var (i, j) in unresolved) radius[i, j] = radius[NearestRow(i, rowHasData), j];
        }

        /// <summary>
        /// 같은 줄에서 표면 거리(칸 차이 x 칸 각도 x 그 칸 반지름, 호 길이)가 가장 가까운 채워진 칸의 값과 그 거리.
        /// 위·아래 줄 후보와 같은 기준(실제 표면 거리)으로 비교하기 위함이다.
        /// </summary>
        bool TryFromRow(bool[,] known, int i, int j, out float value, out float distance)
        {
            const float BinAngle = 2f * Mathf.PI / AngleBins;
            value = 0f;
            distance = float.MaxValue;
            for (int k = 0; k < AngleBins; k++)
            {
                if (!known[i, k]) continue;
                float arc = CircularDistance(j, k) * BinAngle * radius[i, k];
                if (arc >= distance) continue;
                distance = arc;
                value = radius[i, k];
            }
            return distance < float.MaxValue;
        }

        /// <summary>같은 각도에서 위·아래로 가장 가까운 채워진 줄의 값(양쪽 있으면 줄 차이로 보간)과 가까운 쪽 거리</summary>
        bool TryFromColumn(bool[,] known, int i, int j, float rowStep, out float value, out float distance)
        {
            int below = i - 1, above = i + 1;
            while (below >= 0 && !known[below, j]) below--;
            while (above < LengthBins && !known[above, j]) above++;
            bool hasBelow = below >= 0, hasAbove = above < LengthBins;
            value = 0f;
            distance = float.MaxValue;
            if (!hasBelow && !hasAbove) return false;
            if (hasBelow && hasAbove) value = Mathf.Lerp(radius[below, j], radius[above, j], (i - below) / (float)(above - below));
            else value = hasBelow ? radius[below, j] : radius[above, j];
            int nearestRows = Mathf.Min(hasBelow ? i - below : int.MaxValue, hasAbove ? above - i : int.MaxValue);
            distance = nearestRows * rowStep;
            return true;
        }

        static int NearestRow(int i, bool[] rowHasData)
        {
            for (int d = 1; d < LengthBins; d++)
            {
                if (i - d >= 0 && rowHasData[i - d]) return i - d;
                if (i + d < LengthBins && rowHasData[i + d]) return i + d;
            }
            return i;
        }

        static int CircularDistance(int a, int b)
        {
            int d = Mathf.Abs(a - b);
            return Mathf.Min(d, AngleBins - d);
        }

        /// <summary>
        /// 마디 범위 밖(t &lt; 0, t &gt; 1) 표본은 끝 줄에 모은다. 끝 줄이 "마디 너머 몸"(골반 등)을 대표해야
        /// 엉덩이 아래로 내려오는 상의 아랫단이 그 굵기를 따라간다. 버리면 끝 줄이 허리 굵기가 되어 아랫단이 치마처럼 퍼졌다(inuinu).
        /// </summary>
        static int LengthIndex(float t)
        {
            return Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(t) * LengthBins), 0, LengthBins - 1);
        }

        static int AngleIndex(float angle)
        {
            return Mathf.Clamp(Mathf.FloorToInt(Wrap(angle) / (2f * Mathf.PI) * AngleBins), 0, AngleBins - 1);
        }

        /// <summary>각도를 [0, 2π)로</summary>
        static float Wrap(float angle)
        {
            float full = 2f * Mathf.PI;
            return ((angle % full) + full) % full;
        }
    }
}
