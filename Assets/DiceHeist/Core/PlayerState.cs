namespace DiceHeist.Core
{
    public enum SeatStatus
    {
        Waiting,
        Folded,
        Called,
        Raised,
    }

    public sealed class PlayerState
    {
        public readonly int Index;
        public readonly string Id;
        public readonly string Name;
        public readonly AiProfile Ai; // 사람이면 null

        public int Chips;
        public readonly int[] Dice = { 1, 2, 3, 4, 5 };
        public int Rerolls;
        public bool Folded;
        /// <summary>참가비를 뺀, 걸기 단계에서 낸 금액.</summary>
        public int Contrib;
        public SeatStatus Status;
        public bool Revealed;
        public bool Winner;

        public bool IsAi => Ai != null;

        public PlayerState(int index, string id, string name, int chips, AiProfile ai)
        {
            Index = index;
            Id = id;
            Name = name;
            Chips = chips;
            Ai = ai;
        }

        internal void ResetForRound()
        {
            Rerolls = 0;
            Folded = false;
            Contrib = 0;
            Status = SeatStatus.Waiting;
            Revealed = false;
            Winner = false;
        }
    }
}
