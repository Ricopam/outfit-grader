using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OutfitGrader
{
    /// <summary>
    /// 제작사마다 다른 본 이름(VRoid J_Bip, Mixamo, MMD 일본어, Blender .L/.R)을 하나의 비교 키로 바꾼다.
    /// 예: "J_Bip_L_UpperArm", "mixamorig:LeftArm", "左腕", "UpperArm.L" -> "upperarm.l"
    /// </summary>
    public static class BoneNameNormalizer
    {
        const string LeftSuffix = ".l";
        const string RightSuffix = ".r";

        // 복제 도구가 붙이는 꼬리 번호: Unity "Head (1)", Blender "Head.001".
        // "Skirt_1", "Skirt_2"처럼 사슬 순번인 경우가 많은 "_숫자"는 지우지 않는다.
        static readonly Regex DuplicateSuffix = new Regex(@"( \(\d+\)|\.\d{3})$", RegexOptions.Compiled);

        static readonly Regex LeftToken = new Regex(@"(^left|^l_|_l_|_l$|\.l$| l$|left$|_left)", RegexOptions.Compiled);
        static readonly Regex RightToken = new Regex(@"(^right|^r_|_r_|_r$|\.r$| r$|right$|_right)", RegexOptions.Compiled);

        static readonly Regex VRoidPrefix = new Regex(@"^j_(?:bip|sec|adj)_([lrc])_", RegexOptions.Compiled);

        static readonly string[] NoisePrefixes = { "j_bip_", "j_sec_", "j_adj_", "def_", "def-", "bip01_", "bip01 " };

        // MMD 일본어 본 이름 -> 공통 키 (좌우는 별도 처리)
        static readonly Dictionary<string, string> JapaneseAliases = new Dictionary<string, string>
        {
            { "センター", "hips" }, { "下半身", "hips" }, { "上半身", "spine" }, { "上半身2", "chest" },
            { "首", "neck" }, { "頭", "head" }, { "肩", "shoulder" }, { "腕", "upperarm" }, { "ひじ", "lowerarm" },
            { "手首", "hand" }, { "足", "upperleg" }, { "ひざ", "lowerleg" }, { "足首", "foot" }, { "つま先", "toes" },
        };

        // 영어 동의어 -> 공통 키 (Mixamo 규칙: Arm=위팔, ForeArm=아래팔, UpLeg=허벅지, Leg=종아리)
        static readonly Dictionary<string, string> EnglishAliases = new Dictionary<string, string>
        {
            { "hip", "hips" }, { "pelvis", "hips" }, { "arm", "upperarm" }, { "forearm", "lowerarm" }, { "elbow", "lowerarm" },
            { "upleg", "upperleg" }, { "thigh", "upperleg" }, { "leg", "lowerleg" }, { "calf", "lowerleg" },
            { "shin", "lowerleg" }, { "knee", "lowerleg" }, { "wrist", "hand" }, { "ankle", "foot" }, { "toe", "toes" },
            { "toebase", "toes" }, { "clavicle", "shoulder" }, { "spine1", "chest" }, { "spine2", "upperchest" },
        };

        public static string Normalize(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return string.Empty;

            string name = StripNamespace(boneName).Trim();
            name = DuplicateSuffix.Replace(name, string.Empty);

            string side = DetectJapaneseSide(ref name);
            name = name.ToLowerInvariant();
            if (side == null) side = DetectSide(ref name);

            foreach (string prefix in NoisePrefixes)
            {
                if (name.StartsWith(prefix)) { name = name.Substring(prefix.Length); break; }
            }

            name = Regex.Replace(name, @"[\s_.\-]", string.Empty);
            name = ResolveAlias(name);
            return side == null ? name : name + side;
        }

        static string StripNamespace(string name)
        {
            int colon = name.LastIndexOf(':');
            if (colon >= 0) name = name.Substring(colon + 1);
            int slash = name.LastIndexOf('/');
            return slash >= 0 ? name.Substring(slash + 1) : name;
        }

        static string DetectJapaneseSide(ref string name)
        {
            if (name.StartsWith("左")) { name = name.Substring(1); return LeftSuffix; }
            if (name.StartsWith("右")) { name = name.Substring(1); return RightSuffix; }
            return null;
        }

        static string DetectSide(ref string name)
        {
            // VRoid 접두사(J_Bip_L_, J_Sec_R_ 등)는 좌우 정보를 담고 있으므로 접두사째 떼어내며 판정한다.
            Match vroid = VRoidPrefix.Match(name);
            if (vroid.Success)
            {
                name = name.Substring(vroid.Length);
                string sideLetter = vroid.Groups[1].Value;
                return sideLetter == "l" ? LeftSuffix : sideLetter == "r" ? RightSuffix : null;
            }

            Match left = LeftToken.Match(name);
            if (left.Success) { name = RemoveToken(name, left); return LeftSuffix; }
            Match right = RightToken.Match(name);
            if (right.Success) { name = RemoveToken(name, right); return RightSuffix; }
            return null;
        }

        static string RemoveToken(string name, Match token)
        {
            return name.Remove(token.Index, token.Length).Trim('_', '.', ' ');
        }

        static string ResolveAlias(string key)
        {
            if (JapaneseAliases.TryGetValue(key, out string jp)) return jp;
            if (EnglishAliases.TryGetValue(key, out string en)) return en;
            return key;
        }
    }
}
