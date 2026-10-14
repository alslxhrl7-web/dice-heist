using TMPro;
using UnityEngine;

namespace DiceHeist.Game
{
    /// <summary>
    /// 화면 글씨용 한글 폰트. 시스템에 깔린 폰트로 TextMeshPro SDF 폰트를 실행 중에 만든다.
    /// SDF는 크기·화면 배율이 바뀌어도 가장자리가 흐려지지 않는다(예전 Legacy Text는 비트맵이라 흐렸다).
    /// 셰이더는 Assets/TextMesh Pro/Resources의 기본 폰트 머티리얼이 참조하고 있어 빌드에도 들어간다.
    /// </summary>
    public static class UiFont
    {
        static readonly string[] Families = { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR" };

        static TMP_FontAsset _korean;

        public static TMP_FontAsset Korean
        {
            get
            {
                if (_korean != null) return _korean;
                foreach (var family in Families)
                {
                    _korean = TMP_FontAsset.CreateFontAsset(family, "Regular");
                    if (_korean == null) continue;
                    _korean.name = family + " SDF (OS)";
                    return _korean;
                }
                // 한글 폰트가 없는 시스템: 영문·숫자만이라도 보이게 기본 폰트를 쓴다
                Debug.LogWarning("한글 시스템 폰트를 찾지 못해 기본 폰트를 씁니다.");
                _korean = TMP_Settings.defaultFontAsset;
                return _korean;
            }
        }
    }
}
