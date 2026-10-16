using UnityEngine;

namespace DiceHeist.Game
{
    /// <summary>테이블에서 일어나는 일. 동물마다 이때 하는 말과 몸짓이 다르다.</summary>
    public enum TableMoment
    {
        Raise,
        Call,
        Fold,
        Win,
        Lose,
        /// <summary>스트레이트 이상을 만들었을 때(내 토끼)</summary>
        BigHand,
        /// <summary>딜러: 판 시작</summary>
        DealerStart,
        /// <summary>딜러: 쇼다운 직전</summary>
        DealerShowdown,
    }

    /// <summary>
    /// 동물별 성격이 드러나는 말풍선 대사와 몸짓. 기획안: 너구리는 레이즈할 때 윙크, 고양이는 폴드할 때 하품, 곰은 지면 대성통곡.
    /// 겉모습 연출일 뿐이고 판단(AI)과는 상관없다. 대사를 고르는 난수도 게임 난수가 아니다.
    /// </summary>
    public static class AnimalLines
    {
        public static Reaction MotionFor(AnimalKind kind, TableMoment m)
        {
            switch (m)
            {
                case TableMoment.Raise: return kind == AnimalKind.Raccoon || kind == AnimalKind.Fox ? Reaction.Wink : Reaction.Bounce;
                case TableMoment.Call: return Reaction.Nod;
                case TableMoment.Fold: return kind == AnimalKind.Cat ? Reaction.Yawn : Reaction.Slump;
                case TableMoment.Win: return Reaction.Cheer;
                case TableMoment.Lose: return kind == AnimalKind.Bear ? Reaction.Sob : Reaction.Shrug;
                case TableMoment.BigHand: return Reaction.Surprise;
                default: return Reaction.Wave;
            }
        }

        /// <summary>이번에 할 말. 콜은 자주 하면 시끄러워 가끔만 말한다(null이면 말풍선 없음).</summary>
        public static string LineFor(AnimalKind kind, TableMoment m, int ante = 0)
        {
            if (m == TableMoment.Call && Random.value > 0.35f) return null;
            var lines = Table(kind, m);
            if (lines == null || lines.Length == 0) return null;
            return string.Format(lines[Random.Range(0, lines.Length)], ante);
        }

        static string[] Table(AnimalKind kind, TableMoment m)
        {
            switch (kind)
            {
                case AnimalKind.Rabbit:
                    switch (m)
                    {
                        case TableMoment.Raise: return new[] { "올려 볼게요!", "좋았어, 올린다!" };
                        case TableMoment.Call: return new[] { "따라갈게요", "저도요!" };
                        case TableMoment.Fold: return new[] { "이번 판은 쉴래요…", "패스할게요" };
                        case TableMoment.Win: return new[] { "해냈다!", "야호!" };
                        case TableMoment.Lose: return new[] { "으으, 아깝다…", "다음엔 꼭!" };
                        case TableMoment.BigHand: return new[] { "귀가 쫑긋!", "와아아!" };
                    }
                    break;
                case AnimalKind.Raccoon: // 능글맞은 블러퍼
                    switch (m)
                    {
                        case TableMoment.Raise: return new[] { "찡긋~ 올려 볼까?", "후훗, 무섭지?", "이건 진짜야~ 아마도?" };
                        case TableMoment.Call: return new[] { "콜~", "궁금한데?" };
                        case TableMoment.Fold: return new[] { "에이, 접는다", "오늘은 봐줄게~" };
                        case TableMoment.Win: return new[] { "봤지? 이게 실력이야", "히히, 잘 먹을게~" };
                        case TableMoment.Lose: return new[] { "쳇, 들켰네", "다음 판은 내 거야" };
                    }
                    break;
                case AnimalKind.Cat: // 냉정한 계산형
                    switch (m)
                    {
                        case TableMoment.Raise: return new[] { "올리죠.", "계산 끝." };
                        case TableMoment.Call: return new[] { "콜.", "받죠." };
                        case TableMoment.Fold: return new[] { "하암…", "패스. 졸리네" };
                        case TableMoment.Win: return new[] { "예상대로.", "당연한 결과야" };
                        case TableMoment.Lose: return new[] { "…흥.", "확률이 틀렸어" };
                    }
                    break;
                case AnimalKind.Fox:
                    switch (m)
                    {
                        case TableMoment.Raise: return new[] { "후후, 올려 볼까", "꼬리가 근질근질" };
                        case TableMoment.Call: return new[] { "따라가 주지", "재밌네" };
                        case TableMoment.Fold: return new[] { "이번엔 양보하지", "때를 기다리지" };
                        case TableMoment.Win: return new[] { "여우는 놓치지 않아", "후후후" };
                        case TableMoment.Lose: return new[] { "다음엔 안 속아", "운이 좋았네" };
                    }
                    break;
                case AnimalKind.Bear: // 보스, 판돈 큰손
                    switch (m)
                    {
                        case TableMoment.Raise: return new[] { "크게 간다!", "판돈을 키우자고!" };
                        case TableMoment.Call: return new[] { "받아 주지", "덤벼라!" };
                        case TableMoment.Fold: return new[] { "흠, 이번만 봐준다", "작전상 후퇴다" };
                        case TableMoment.Win: return new[] { "우하하! 내 거다!", "보스는 안 져!" };
                        case TableMoment.Lose: return new[] { "으아앙! 내 칩!", "말도 안 돼애!" };
                    }
                    break;
                case AnimalKind.Penguin: // 딜러
                    switch (m)
                    {
                        case TableMoment.DealerStart: return new[] { "앤티 {0}칩씩 받겠습니다", "새 판 시작합니다" };
                        case TableMoment.DealerShowdown: return new[] { "쇼다운!", "컵을 열겠습니다" };
                    }
                    break;
            }
            return null;
        }
    }
}
