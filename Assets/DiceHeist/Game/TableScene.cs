using System.Collections;
using UnityEngine;

namespace DiceHeist.Game
{
    /// <summary>테이블, 세 자리의 동물·주사위·컵, 판돈 칩 더미를 코드로 조립하고 자리별 연출을 제공한다.</summary>
    public sealed class TableScene : MonoBehaviour
    {
        public sealed class Seat
        {
            public AnimalActor Animal;
            public DieView[] Dice;
            /// <summary>상대 자리의 주사위 컵. 내 자리는 null.</summary>
            public Transform Cup;
            public Vector3 CupRest;
            public Vector3 RowCenter;
            public float RowSpacing;
            public Transform LabelAnchor;
            /// <summary>말풍선이 뜨는 자리. 동물이 바뀌어도 그대로다.</summary>
            public Transform BubbleAnchor;
            public AnimalKind Kind;
        }

        public const float FloorY = -0.9f;
        /// <summary>동물이 앉은 높이(의자 위). 바닥에 서 있으면 탁자 가장자리에 몸이 가려 머리만 보였다</summary>
        public const float SeatY = -0.15f;
        static readonly Color Stool = new Color(0.55f, 0.40f, 0.29f);
        const float MyDieSize = 0.5f;
        const float OppDieSize = 0.5f;

        static readonly Color Felt = new Color(0.44f, 0.68f, 0.55f);
        static readonly Color FeltDark = new Color(0.36f, 0.59f, 0.47f);
        static readonly Color Wood = new Color(0.62f, 0.46f, 0.33f);
        static readonly Color DieBody = new Color(1f, 0.99f, 0.96f);
        static readonly Color DiePip = new Color(0.15f, 0.19f, 0.17f);
        static readonly Color Gold = new Color(0.89f, 0.64f, 0.15f);
        static readonly Color Rose = new Color(0.77f, 0.26f, 0.42f);

        // 내 주사위가 구르는 구역
        static readonly Vector3 ZoneCenter = new Vector3(0.4f, 0f, -1.3f);
        static readonly Vector2 ZoneHalf = new Vector2(2.3f, 1.15f);

        public readonly Seat[] Seats = new Seat[3];
        /// <summary>테이블 머리(상대 둘 사이 뒤쪽)에 서 있는 딜러. 판에는 끼지 않는다.</summary>
        public AnimalActor Dealer { get; private set; }
        AnimalKind _dealerKind;
        static readonly Vector3 DealerSpot = new Vector3(0f, SeatY, 5.6f);
        /// <summary>딜러 말풍선 자리(얼굴 바로 아래, 테이블 위).</summary>
        public Transform DealerBubbleAnchor { get; private set; }
        public Transform PotAnchor { get; private set; }
        Transform _potPile;
        int _potShown = -1;

        public static TableScene Build()
        {
            var scene = new GameObject("TableScene").AddComponent<TableScene>();
            var root = scene.transform;

            // 바닥과 테이블
            Fx.Prim(PrimitiveType.Plane, root, new Vector3(0, FloorY, 0), Vector3.one * 8f, new Color(0.72f, 0.79f, 0.75f), name: "Floor");
            Fx.Prim(PrimitiveType.Cylinder, root, new Vector3(0, -0.2f, 0), new Vector3(9.7f, 0.18f, 9.7f), Wood, name: "TableRim");
            Fx.Prim(PrimitiveType.Cylinder, root, new Vector3(0, -0.1f, 0), new Vector3(9.2f, 0.1f, 9.2f), Felt, name: "TableFelt");
            Fx.Prim(PrimitiveType.Cylinder, root, new Vector3(0, -0.6f, 0), new Vector3(2.4f, 0.3f, 2.4f), Wood, name: "TableLeg");

            var floorCol = new GameObject("TableCollider").AddComponent<BoxCollider>();
            floorCol.transform.SetParent(root, false);
            floorCol.center = new Vector3(0, -0.5f, 0);
            floorCol.size = new Vector3(30, 1, 30);

            scene.BuildRollZone();

            // 자리 0: 나(토끼, 왼쪽 앞 테이블 가장자리. 카메라에 옆모습이 보이게) / 1: 너구리 / 2: 고양이
            scene.Seats[0] = scene.BuildSeat(0, AnimalKind.Rabbit, new Vector3(-4.9f, SeatY, -2.2f), true);
            scene.Seats[1] = scene.BuildSeat(1, AnimalKind.Raccoon, new Vector3(-3.3f, SeatY, 4.2f), false);
            scene.Seats[2] = scene.BuildSeat(2, AnimalKind.Cat, new Vector3(3.3f, SeatY, 4.2f), false);
            // 동물이 앉는 둥근 의자(바닥에서 앉은 높이까지)
            foreach (var s in scene.Seats) scene.AddStool(s.Animal.Seat);
            scene.AddStool(DealerSpot);

            scene.Seats[0].RowCenter = new Vector3(0.4f, MyDieSize * 0.5f, -3.3f);
            scene.Seats[0].RowSpacing = 0.74f;

            scene.DealerBubbleAnchor = Fx.Pivot("DealerBubble", root, DealerSpot + new Vector3(0f, 2.1f, -1.4f));
            scene.PotAnchor = Fx.Pivot("PotAnchor", root, new Vector3(0, 0.2f, 1.1f));
            scene._potPile = Fx.Pivot("PotPile", root, new Vector3(0, 0, 0.2f));
            return scene;
        }

        void BuildRollZone()
        {
            var zone = Fx.Pivot("RollZone", transform, ZoneCenter);
            Fx.Prim(PrimitiveType.Cube, zone, new Vector3(0, 0.004f, 0), new Vector3(ZoneHalf.x * 2, 0.008f, ZoneHalf.y * 2), FeltDark, name: "Mat");
            // 낮은 난간은 보이기만 하고, 실제로 막는 벽은 보이지 않게 높이 세운다
            Wall(zone, new Vector3(-ZoneHalf.x, 0, 0), new Vector3(0.12f, 0.16f, ZoneHalf.y * 2 + 0.12f));
            Wall(zone, new Vector3(ZoneHalf.x, 0, 0), new Vector3(0.12f, 0.16f, ZoneHalf.y * 2 + 0.12f));
            Wall(zone, new Vector3(0, 0, -ZoneHalf.y), new Vector3(ZoneHalf.x * 2 + 0.12f, 0.16f, 0.12f));
            Wall(zone, new Vector3(0, 0, ZoneHalf.y), new Vector3(ZoneHalf.x * 2 + 0.12f, 0.16f, 0.12f));
        }

        static void Wall(Transform zone, Vector3 pos, Vector3 railSize)
        {
            Fx.Prim(PrimitiveType.Cube, zone, pos + Vector3.up * railSize.y * 0.5f, railSize, Wood, name: "Rail");
            var col = new GameObject("Wall").AddComponent<BoxCollider>();
            col.transform.SetParent(zone, false);
            col.transform.localPosition = pos + Vector3.up * 3f;
            col.size = new Vector3(Mathf.Max(railSize.x, 0.4f), 6f, Mathf.Max(railSize.z, 0.4f));
        }

        Seat BuildSeat(int index, AnimalKind kind, Vector3 seatPos, bool mine)
        {
            var seat = new Seat { Kind = kind };
            var animal = AnimalBuilder.Build(kind, transform);
            animal.Seat = seatPos;
            animal.transform.position = seatPos;
            animal.FaceTowards(mine ? new Vector3(-1.0f, 0, -5.5f) : Vector3.zero); // 토끼는 탁자와 카메라 사이를 봐서 얼굴이 보이게
            animal.RememberFacing();
            seat.Animal = animal;
            // 이름표는 동물을 가리지 않게: 내 것은 머리 위, 상대는 바깥쪽 옆
            seat.LabelAnchor = Fx.Pivot("LabelAnchor", transform, seatPos + (mine ? new Vector3(-0.1f, 3.4f, 0) : new Vector3(Mathf.Sign(seatPos.x) * 2.9f, 0.9f, 0)));
            // 말풍선은 테이블 안쪽, 머리 높이
            Vector3 inward = -seatPos;
            inward.y = 0;
            seat.BubbleAnchor = Fx.Pivot("BubbleAnchor", transform,
                seatPos + (mine ? new Vector3(1.3f, 2.1f, 0.6f) : inward.normalized * 1.3f + Vector3.up * 1.9f));

            float size = mine ? MyDieSize : OppDieSize;
            seat.Dice = new DieView[5];
            for (int i = 0; i < 5; i++)
            {
                var die = DieView.Create($"Die_{kind}_{i}", i, size, DieBody, DiePip, mine);
                die.transform.SetParent(transform, false);
                die.gameObject.SetActive(false);
                seat.Dice[i] = die;
            }

            if (!mine)
            {
                Vector3 toCenter = (Vector3.zero - seatPos);
                toCenter.y = 0;
                seat.CupRest = new Vector3(seatPos.x, 0, seatPos.z) + toCenter.normalized * 2.6f;
                seat.RowCenter = seat.CupRest + Vector3.up * (OppDieSize * 0.5f);
                seat.RowSpacing = 0.62f;

                seat.Cup = Fx.Pivot("Cup_" + kind, transform, seat.CupRest);
                Fx.Prim(PrimitiveType.Cylinder, seat.Cup, new Vector3(0, 0.45f, 0), new Vector3(1.0f, 0.42f, 1.0f),
                    kind == AnimalKind.Raccoon ? Rose : new Color(0.33f, 0.45f, 0.62f), name: "Shell");
                Fx.Prim(PrimitiveType.Cylinder, seat.Cup, new Vector3(0, 0.05f, 0), new Vector3(1.12f, 0.05f, 1.12f), Gold, name: "Lip");
            }
            return seat;
        }

        /// <summary>Characters.Ids("fox" 등)를 AnimalKind로. 모르는 id면 fallback.</summary>
        void AddStool(Vector3 seat)
        {
            float h = SeatY - FloorY;
            Fx.Prim(PrimitiveType.Cylinder, transform, new Vector3(seat.x, FloorY + h * 0.5f, seat.z), new Vector3(1.5f, h * 0.5f, 1.5f), Stool, name: "Stool");
        }

        public static AnimalKind KindOf(string id, AnimalKind fallback) =>
            !string.IsNullOrEmpty(id) && System.Enum.TryParse(id, true, out AnimalKind k) ? k : fallback;

        /// <summary>
        /// 테이블 등급에 맞춰 상대 동물과 딜러를 바꾼다(브론즈 너구리·고양이, 실버 여우·너구리, 골드 곰·여우, 딜러 펭귄).
        /// 상대가 바뀌었으면 true(다시 등장 연출을 하라는 뜻). 겉모습만 바뀌고 주사위·컵·자리는 그대로다.
        /// </summary>
        public bool SetCast(string opp1, string opp2, string dealer)
        {
            bool changed = Swap(1, KindOf(opp1, AnimalKind.Raccoon)) | Swap(2, KindOf(opp2, AnimalKind.Cat));
            var dk = KindOf(dealer, AnimalKind.Penguin);
            if (Dealer == null || _dealerKind != dk)
            {
                if (Dealer != null) Destroy(Dealer.gameObject);
                Dealer = AnimalBuilder.Build(dk, transform);
                Dealer.Seat = DealerSpot;
                Dealer.transform.position = DealerSpot;
                Dealer.FaceTowards(new Vector3(0f, 0f, -4.5f));
                Dealer.Kind = dk;
                Dealer.RememberFacing();
                _dealerKind = dk;
            }
            return changed;
        }

        bool Swap(int index, AnimalKind kind)
        {
            var seat = Seats[index];
            if (seat.Kind == kind) return false;
            var old = seat.Animal;
            var animal = AnimalBuilder.Build(kind, transform);
            animal.Seat = old.Seat;
            animal.transform.position = old.Seat;
            animal.FaceTowards(Vector3.zero);
            animal.RememberFacing();
            seat.Animal = animal;
            seat.Kind = kind;
            Destroy(old.gameObject);
            return true;
        }

        public Vector3 RowSlot(int seat, int i) =>
            Seats[seat].RowCenter + Vector3.right * ((i - 2) * Seats[seat].RowSpacing);

        // ---------- 연출 ----------

        /// <summary>동물들이 테이블 밖에서 깡충깡충 뛰어 자리로 나온다.</summary>
        public IEnumerator Entrance()
        {
            var routines = new IEnumerator[Seats.Length];
            for (int i = 0; i < Seats.Length; i++)
            {
                var a = Seats[i].Animal;
                Vector3 outward = a.Seat;
                outward.y = 0;
                a.PlaceAway(a.Seat + outward.normalized * 7f);
                routines[i] = DelayedHop(a, i * 0.25f);
            }
            yield return Fx.All(this, routines);
        }

        static IEnumerator DelayedHop(AnimalActor a, float delay)
        {
            yield return new WaitForSeconds(delay);
            yield return a.HopTo(a.Seat, 5, 1.4f);
        }

        /// <summary>판 시작: 주사위를 치우고 컵과 자세를 되돌린다.</summary>
        public void ResetForRound()
        {
            foreach (var seat in Seats)
            {
                foreach (var d in seat.Dice)
                {
                    d.Held = false;
                    d.Pickable = false;
                    d.gameObject.SetActive(false);
                }
                if (seat.Cup != null) seat.Cup.SetPositionAndRotation(seat.CupRest, Quaternion.identity);
                seat.Animal.SetDroop(false);
            }
        }

        /// <summary>내 주사위 중 idxs 자리를 토끼가 집어 던지고, 멈추면 앞줄에 가지런히 놓는다.</summary>
        public IEnumerator ThrowMine(int[] idxs, int[] values)
        {
            var seat = Seats[0];
            var rabbit = seat.Animal;
            var dice = new DieView[idxs.Length];
            var vals = new int[idxs.Length];
            for (int k = 0; k < idxs.Length; k++)
            {
                dice[k] = seat.Dice[idxs[k]];
                vals[k] = values[idxs[k]];
            }

            // 손으로 모으기
            Vector3 hand = rabbit.HandPosition;
            var gather = new IEnumerator[dice.Length];
            for (int k = 0; k < dice.Length; k++)
            {
                var d = dice[k];
                if (!d.gameObject.activeSelf)
                {
                    d.transform.position = hand;
                    d.transform.localScale = Vector3.one * 0.3f;
                    d.gameObject.SetActive(true);
                    gather[k] = Fx.ScaleTo(d.transform, Vector3.one * 0.3f, 0.01f);
                }
                else gather[k] = Gather(d, hand);
            }
            yield return Fx.All(this, gather);

            StartCoroutine(rabbit.Throw());
            yield return new WaitForSeconds(0.2f);

            foreach (var d in dice) d.transform.localScale = Vector3.one;
            Vector3 origin = ZoneCenter + new Vector3(-ZoneHalf.x + 0.6f, 1.0f, 0f);
            yield return DiceThrower.Throw(dice, vals, origin, Vector3.right);
            yield return new WaitForSeconds(0.25f);

            // 앞줄로 정렬
            var line = new IEnumerator[dice.Length];
            for (int k = 0; k < dice.Length; k++)
                line[k] = Fx.MoveTo(dice[k].transform, RowSlot(0, idxs[k]), DieView.Upright(dice[k].UpFace()), 0.3f, 0.35f);
            yield return Fx.All(this, line);
            Physics.SyncTransforms();
        }

        static IEnumerator Gather(DieView d, Vector3 hand)
        {
            yield return Fx.MoveTo(d.transform, hand, d.transform.rotation, 0.22f, 0.5f);
            d.transform.localScale = Vector3.one * 0.3f;
        }

        /// <summary>상대가 컵을 들어 흔들고 내려놓는다. 주사위는 컵 안이라 보이지 않는다.</summary>
        public IEnumerator ShakeCup(int seatIndex)
        {
            var seat = Seats[seatIndex];
            var cup = seat.Cup;
            Vector3 hold = Vector3.Lerp(seat.CupRest, seat.Animal.HandPosition, 0.75f) + Vector3.up * 1.0f;
            yield return Fx.MoveTo(cup, hold, Quaternion.Euler(0, 0, 160f), 0.22f, 0.3f);
            StartCoroutine(seat.Animal.Throw());
            DiceAudio.CupRattle(0.55f);
            for (float e = 0; e < 0.55f; e += Time.deltaTime)
            {
                float s = Mathf.Sin(e * 42f);
                cup.SetPositionAndRotation(hold + Vector3.up * (s * 0.16f), Quaternion.Euler(s * 14f, 0, 160f + s * 10f));
                yield return null;
            }
            yield return Fx.MoveTo(cup, seat.CupRest, Quaternion.identity, 0.16f, 0.2f);
            DiceAudio.CupSlam();
        }

        /// <summary>공개: 컵을 치우고 주사위 5개를 줄지어 보여준다.</summary>
        /// <param name="slow">마지막 공개처럼 뜸을 들일 때: 컵을 살짝 들었다 멈췄다가 연다.</param>
        public IEnumerator Reveal(int seatIndex, int[] values, bool slow = false)
        {
            var seat = Seats[seatIndex];
            Vector3 back = (seat.Animal.Seat - seat.CupRest);
            back.y = 0;
            if (slow)
            {
                // 살짝 들어 틈만 보여 주고 흔들며 뜸을 들인다
                yield return Fx.MoveTo(seat.Cup, seat.CupRest + Vector3.up * 0.18f, Quaternion.identity, 0.35f);
                for (float e = 0; e < 0.6f; e += Time.deltaTime)
                {
                    seat.Cup.rotation = Quaternion.Euler(Mathf.Sin(e * 30f) * 3f, 0, Mathf.Cos(e * 26f) * 3f);
                    yield return null;
                }
            }
            DiceAudio.Tock(slow ? 0.85f : 1f);
            StartCoroutine(Fx.MoveTo(seat.Cup, seat.CupRest + back.normalized * 1.25f + Vector3.up * 0.02f, Quaternion.Euler(0, 0, 0), 0.35f, 1.2f));

            var routines = new IEnumerator[5];
            for (int i = 0; i < 5; i++)
            {
                var d = seat.Dice[i];
                d.transform.SetPositionAndRotation(seat.RowCenter, Quaternion.identity);
                d.transform.localScale = Vector3.one * 0.2f;
                d.ShowValueOnFace(values[i], 1);
                d.gameObject.SetActive(true);
                routines[i] = Fx.All(this,
                    Fx.MoveTo(d.transform, RowSlot(seatIndex, i), Quaternion.identity, 0.3f, 0.5f),
                    Fx.ScaleTo(d.transform, Vector3.one, 0.3f));
            }
            yield return Fx.All(this, routines);
        }

        /// <summary>족보를 이루는 주사위를 두 번 통통 튀게 해 눈에 띄게 한다.</summary>
        public IEnumerator Highlight(int seatIndex, bool[] mask)
        {
            var dice = Seats[seatIndex].Dice;
            var basePos = new Vector3[dice.Length];
            for (int i = 0; i < dice.Length; i++) basePos[i] = dice[i].transform.position;
            for (int hop = 0; hop < 2; hop++)
            {
                for (float e = 0; e < 0.28f; e += Time.deltaTime)
                {
                    float h = Mathf.Sin(e / 0.28f * Mathf.PI);
                    for (int i = 0; i < dice.Length; i++)
                    {
                        if (!mask[i] || !dice[i].gameObject.activeSelf) continue;
                        dice[i].transform.position = basePos[i] + Vector3.up * (h * 0.35f);
                        dice[i].transform.localScale = Vector3.one * (1f + h * 0.18f);
                    }
                    yield return null;
                }
            }
            for (int i = 0; i < dice.Length; i++)
            {
                if (!dice[i].gameObject.activeSelf) continue;
                dice[i].transform.position = basePos[i];
                dice[i].transform.localScale = Vector3.one;
            }
            Physics.SyncTransforms();
        }

        /// <summary>가운데 칩 더미가 이긴 사람(들) 쪽으로 날아가 사라진다. 짤그랑 소리와 함께.</summary>
        public IEnumerator PotTo(int[] seats)
        {
            if (seats == null || seats.Length == 0 || _potPile.childCount == 0) yield break;
            var chips = new Transform[_potPile.childCount];
            for (int i = 0; i < chips.Length; i++) chips[i] = _potPile.GetChild(i);
            // 칩 더미에서 먼저 떼어 낸다. 날아가는 도중에 SetPot이 더미를 다시 만들어도 날아가는 칩은 지워지지 않는다
            foreach (var c in chips) c.SetParent(transform, true);
            _potShown = 0;
            var routines = new IEnumerator[chips.Length];
            for (int i = 0; i < chips.Length; i++)
            {
                var target = Seats[seats[i % seats.Length]].Animal.Seat + Vector3.up * 1.1f;
                routines[i] = FlyChip(chips[i], target, i * 0.025f);
            }
            DiceAudio.Chips(Mathf.Min(8, chips.Length), 0.8f);
            yield return Fx.All(this, routines);
        }

        IEnumerator FlyChip(Transform chip, Vector3 target, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (chip == null) yield break;
            yield return Fx.All(this,
                Fx.MoveTo(chip, target, chip.rotation, 0.45f, 1.4f),
                Fx.ScaleTo(chip, chip.localScale * 0.4f, 0.45f));
            chip.gameObject.SetActive(false);
            Destroy(chip.gameObject);
        }

        /// <summary>판돈만큼 가운데에 칩을 쌓는다.</summary>
        public void SetPot(int chips, int ante)
        {
            int n = Mathf.Clamp(chips / Mathf.Max(1, ante), 0, 36);
            if (n == _potShown) return;
            _potShown = n;
            for (int i = _potPile.childCount - 1; i >= 0; i--) Destroy(_potPile.GetChild(i).gameObject);
            for (int i = 0; i < n; i++)
            {
                int stack = i / 6, level = i % 6;
                var pos = new Vector3((stack % 3 - 1) * 0.5f + (stack / 3) * 0.25f, 0.03f + level * 0.062f, -(stack / 3) * 0.45f);
                Fx.Prim(PrimitiveType.Cylinder, _potPile, pos, new Vector3(0.42f, 0.028f, 0.42f), (stack + level) % 2 == 0 ? Gold : Rose, name: "Chip");
            }
        }
    }
}
