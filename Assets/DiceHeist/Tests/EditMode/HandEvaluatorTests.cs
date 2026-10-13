using System.Collections.Generic;
using DiceHeist.Core;
using NUnit.Framework;

namespace DiceHeist.Tests
{
    public class HandEvaluatorTests
    {
        static int[] D(string s)
        {
            var d = new int[s.Length];
            for (int i = 0; i < s.Length; i++) d[i] = s[i] - '0';
            return d;
        }

        [Test]
        public void 전체_7776가지_족보별_개수()
        {
            var counts = new int[8];
            var d = new int[5];
            int total = 0;
            for (d[0] = 1; d[0] <= 6; d[0]++)
            for (d[1] = 1; d[1] <= 6; d[1]++)
            for (d[2] = 1; d[2] <= 6; d[2]++)
            for (d[3] = 1; d[3] <= 6; d[3]++)
            for (d[4] = 1; d[4] <= 6; d[4]++)
            {
                counts[(int)HandEvaluator.Evaluate(d).Rank]++;
                total++;
            }

            Assert.AreEqual(7776, total);
            Assert.AreEqual(480, counts[(int)HandRank.NoPair], "노페어");
            Assert.AreEqual(3600, counts[(int)HandRank.OnePair], "원페어");
            Assert.AreEqual(1800, counts[(int)HandRank.TwoPair], "투페어");
            Assert.AreEqual(1200, counts[(int)HandRank.Triple], "트리플");
            Assert.AreEqual(300, counts[(int)HandRank.FullHouse], "풀하우스");
            Assert.AreEqual(240, counts[(int)HandRank.Straight], "스트레이트");
            Assert.AreEqual(150, counts[(int)HandRank.FourDice], "포 다이스");
            Assert.AreEqual(6, counts[(int)HandRank.FiveDice], "파이브 다이스");
        }

        [TestCase("44444", HandRank.FiveDice)]
        [TestCase("66662", HandRank.FourDice)]
        [TestCase("23456", HandRank.Straight)]
        [TestCase("54321", HandRank.Straight)]
        [TestCase("55533", HandRank.FullHouse)]
        [TestCase("22256", HandRank.Triple)]
        [TestCase("66331", HandRank.TwoPair)]
        [TestCase("44136", HandRank.OnePair)]
        [TestCase("12456", HandRank.NoPair)]
        [TestCase("12346", HandRank.NoPair)]
        public void 기획안_예시_족보(string dice, HandRank expected)
        {
            Assert.AreEqual(expected, HandEvaluator.Evaluate(D(dice)).Rank);
        }

        [Test]
        public void 족보_순위는_파이브_포_스트레이트_풀하우스_순()
        {
            string[] highToLow = { "44444", "66662", "12345", "66655", "66612", "66551", "66123", "12356" };
            for (int i = 0; i + 1 < highToLow.Length; i++)
                Assert.Greater(HandEvaluator.Compare(D(highToLow[i]), D(highToLow[i + 1])), 0,
                    $"{highToLow[i]} > {highToLow[i + 1]}");
        }

        // a가 이겨야 하는 동점 족보 쌍
        [TestCase("66666", "55555", TestName = "파이브: 눈이 높은 쪽")]
        [TestCase("66661", "33336", TestName = "포: 6 포 다이스 > 3 포 다이스")]
        [TestCase("44446", "44442", TestName = "포: 같은 묶음이면 남은 주사위")]
        [TestCase("23456", "12345", TestName = "스트레이트: 2-6 > 1-5")]
        [TestCase("55522", "44466", TestName = "풀하우스: 세 개 묶음 먼저")]
        [TestCase("55533", "55522", TestName = "풀하우스: 세 개가 같으면 두 개 묶음")]
        [TestCase("33312", "22265", TestName = "트리플: 묶음 눈 먼저")]
        [TestCase("33361", "33354", TestName = "트리플: 남은 주사위 높은 것부터")]
        [TestCase("33365", "33364", TestName = "트리플: 두 번째 남은 주사위")]
        [TestCase("66221", "55443", TestName = "투페어: 큰 묶음 먼저")]
        [TestCase("66331", "66225", TestName = "투페어: 큰 묶음이 같으면 작은 묶음")]
        [TestCase("66335", "66331", TestName = "투페어: 묶음이 같으면 남은 주사위")]
        [TestCase("44123", "33654", TestName = "원페어: 묶음 눈 먼저")]
        [TestCase("44621", "44531", TestName = "원페어: 남은 주사위 높은 것부터")]
        [TestCase("44632", "44631", TestName = "원페어: 마지막 남은 주사위")]
        [TestCase("12456", "12356", TestName = "노페어: 높은 눈부터 차례로")]
        public void 동점이면_눈으로_가린다(string a, string b)
        {
            Assert.Greater(HandEvaluator.Compare(D(a), D(b)), 0);
            Assert.Less(HandEvaluator.Compare(D(b), D(a)), 0);
        }

        [TestCase("66331", "13636")]
        [TestCase("55533", "35355")]
        [TestCase("12345", "54321")]
        [TestCase("44136", "63144")]
        public void 눈_구성이_같으면_순서와_무관하게_완전_동점(string a, string b)
        {
            Assert.AreEqual(0, HandEvaluator.Compare(D(a), D(b)));
        }

        [Test]
        public void 비교는_반대칭이고_같은_구성끼리만_0()
        {
            var all = new List<int[]>();
            var d = new int[5];
            // 정렬된 조합만(252가지) 뽑아 서로 비교
            for (d[0] = 1; d[0] <= 6; d[0]++)
            for (d[1] = d[0]; d[1] <= 6; d[1]++)
            for (d[2] = d[1]; d[2] <= 6; d[2]++)
            for (d[3] = d[2]; d[3] <= 6; d[3]++)
            for (d[4] = d[3]; d[4] <= 6; d[4]++)
                all.Add((int[])d.Clone());

            Assert.AreEqual(252, all.Count);
            for (int i = 0; i < all.Count; i++)
            for (int j = 0; j < all.Count; j++)
            {
                int ab = HandEvaluator.Compare(all[i], all[j]);
                int ba = HandEvaluator.Compare(all[j], all[i]);
                Assert.AreEqual(System.Math.Sign(ab), -System.Math.Sign(ba));
                if (i != j) Assert.AreNotEqual(0, ab, $"{string.Join("", all[i])} vs {string.Join("", all[j])}");
            }
        }

        [Test]
        public void 족보를_이루는_주사위_표시()
        {
            CollectionAssert.AreEqual(new[] { true, true, false, false, false }, HandEvaluator.PartMask(D("44136")));
            CollectionAssert.AreEqual(new[] { true, true, true, true, true }, HandEvaluator.PartMask(D("23456")));
            CollectionAssert.AreEqual(new[] { false, false, false, false, false }, HandEvaluator.PartMask(D("12356")));
        }
    }
}
