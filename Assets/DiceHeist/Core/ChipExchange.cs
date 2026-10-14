using System;

namespace DiceHeist.Core
{
    /// <summary>
    /// 싱글(AI 테이블)에서 번 칩을 멀티에서 쓸 사이버머니로 바꾼다. 한 방향뿐이다: 사이버머니를 칩이나 현금으로 되돌리지 않는다.
    /// 처음 받은 500칩과 구제 칩은 바꿀 수 없다. 바닥나면 몇 번이든 나오는 구제 칩이 사이버머니로 새지 않게 하려는 것이다.
    /// </summary>
    public static class ChipExchange
    {
        /// <summary>칩 1개당 사이버머니.</summary>
        public const int Rate = 100;
        /// <summary>한 번에 바꾸는 칩 단위.</summary>
        public const int Unit = 100;
        /// <summary>이만큼은 남겨야 한다(브론즈 시작 칩). 이 위로 번 칩만 바꿀 수 있다.</summary>
        public static int Floor => TableConfig.Bronze.StartChips;

        /// <summary>지금 바꿀 수 있는 최대 칩(Unit 단위).</summary>
        public static int Exchangeable(int chips) => Math.Max(0, chips - Floor) / Unit * Unit;

        /// <summary>chips 중 amount칩을 사이버머니로 바꾼다. 바꾼 뒤의 칩과 받은 사이버머니를 돌려준다.</summary>
        public static (int chipsLeft, long money) Exchange(int chips, int amount)
        {
            if (amount <= 0 || amount % Unit != 0) throw new ArgumentException($"{Unit}칩 단위로만 바꿀 수 있습니다.", nameof(amount));
            if (amount > Exchangeable(chips)) throw new InvalidOperationException($"처음 받은 {Floor}칩 위로 번 칩만 바꿀 수 있습니다.");
            return (chips - amount, (long)amount * Rate);
        }
    }
}
