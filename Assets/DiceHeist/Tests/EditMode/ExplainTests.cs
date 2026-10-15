using DiceHeist.Core;
using NUnit.Framework;

namespace DiceHeist.Tests
{
    /// <summary>결과 화면의 "이긴 이유" 한 줄.</summary>
    public class ExplainTests
    {
        static HandValue H(string s)
        {
            var d = new int[5];
            for (int i = 0; i < 5; i++) d[i] = s[i] - '0';
            return HandEvaluator.Evaluate(d);
        }

        [TestCase("12345", "66655", "스트레이트가 풀하우스보다 높은 족보")]
        [TestCase("22256", "66331", "트리플이 투페어보다 높은 족보")]
        [TestCase("66661", "33336", "같은 포 다이스 · 포 다이스 눈 6 > 3")]
        [TestCase("44446", "44442", "같은 포 다이스 · 남은 주사위 6 > 2")]
        [TestCase("23456", "12345", "같은 스트레이트 · 2-6이 1-5보다 높음")]
        [TestCase("55522", "44466", "같은 풀하우스 · 트리플 눈 5 > 4")]
        [TestCase("55533", "55522", "같은 풀하우스 · 페어 눈 3 > 2")]
        [TestCase("66221", "55443", "같은 투페어 · 큰 페어 6 > 5")]
        [TestCase("66331", "66225", "같은 투페어 · 작은 페어 3 > 2")]
        [TestCase("66335", "66331", "같은 투페어 · 남은 주사위 5 > 1")]
        [TestCase("44632", "44631", "같은 원페어 · 남은 주사위 2 > 1")]
        [TestCase("12456", "12356", "같은 노페어 · 다음 높은 눈 4 > 3")]
        [TestCase("66331", "13636", "같은 투페어 · 완전 동점")]
        public void 이긴_이유(string winner, string loser, string expected)
        {
            Assert.AreEqual(expected, HandEvaluator.Explain(H(winner), H(loser)));
        }

        [Test]
        public void 이유는_실제_승패와_같은_방향이다()
        {
            // 이긴 쪽을 넣었을 때 설명의 왼쪽 숫자가 항상 크다(완전 동점 제외)
            var a = H("33361");
            var b = H("33354");
            Assert.Greater(HandEvaluator.Compare(a, b), 0);
            StringAssert.Contains("6 > 5", HandEvaluator.Explain(a, b));
        }
    }
}
