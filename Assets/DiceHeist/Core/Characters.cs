namespace DiceHeist.Core
{
    /// <summary>
    /// 고를 수 있는 동물 캐릭터. 겉모습만 다르고 족보 확률·판정에는 아무 영향이 없다(기획 원칙).
    /// 서버(계정에 저장)와 Unity(AnimalKind로 바꿔 모델을 만든다)·웹(얼굴 그림)이 같은 id를 쓴다.
    /// </summary>
    public static class Characters
    {
        public static readonly string[] Ids = { "rabbit", "raccoon", "cat", "fox", "bear", "penguin" };
        public static readonly string[] Names = { "토끼", "너구리", "고양이", "여우", "곰", "펭귄" };

        public static bool IsValid(string id) => System.Array.IndexOf(Ids, id) >= 0;

        public static string NameOf(string id)
        {
            int i = System.Array.IndexOf(Ids, id);
            return i >= 0 ? Names[i] : Names[0];
        }

        /// <summary>새 계정의 처음 캐릭터. 방에 같은 동물만 앉지 않게 번호대로 돌린다(나중에 바꿀 수 있다).</summary>
        public static string DefaultFor(long userId) => Ids[(int)((userId - 1) % Ids.Length + Ids.Length) % Ids.Length];
    }
}
