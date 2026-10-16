using System.Collections;
using UnityEngine;

namespace DiceHeist.Game
{
    /// <summary>동물 몸짓의 종류. 어떤 동물이 무엇을 할지는 AnimalLines가 고른다.</summary>
    public enum Reaction
    {
        /// <summary>고개 끄덕(콜)</summary>
        Nod,
        /// <summary>통 튀어 오름(레이즈)</summary>
        Bounce,
        /// <summary>갸웃하며 찡긋(너구리·여우 레이즈)</summary>
        Wink,
        /// <summary>쭉 늘어나며 하품(고양이 폴드)</summary>
        Yawn,
        /// <summary>움찔 주저앉음(폴드)</summary>
        Slump,
        /// <summary>세 번 뛰고 한 바퀴(승리)</summary>
        Cheer,
        /// <summary>부들부들 떨며 엉엉(곰 패배)</summary>
        Sob,
        /// <summary>어깨 으쓱(패배)</summary>
        Shrug,
        /// <summary>깜짝 놀라 쫑긋(큰 족보)</summary>
        Surprise,
        /// <summary>좌우로 흔들며 인사(딜러)</summary>
        Wave,
    }

    /// <summary>
    /// 동물 하나의 등장·던지기·리액션 연출. 승패에는 영향이 없다.
    /// 3D 모델은 한 덩어리라 머리·팔을 따로 못 움직이므로, 몸 전체의 기울기·높이·늘고 줄기로 표정을 낸다.
    /// 연출 코루틴은 아래 값(_lean 등)만 바꾸고, 실제 자세는 Update가 한곳에서 합쳐 적용한다(서로 덮어쓰지 않게).
    /// </summary>
    public sealed class AnimalActor : MonoBehaviour
    {
        public Transform Head;
        public Transform ArmR;
        public Transform Body;
        public AnimalKind Kind;
        /// <summary>자리에 섰을 때의 위치.</summary>
        public Vector3 Seat;

        float _phase;
        bool _droop;
        float _droopBlend;
        bool _hopping;
        Coroutine _reaction;

        // 연출이 바꾸는 자세 값
        float _lean;   // 앞(+)·뒤(-) 기울기
        float _roll;   // 옆 기울기
        float _yaw;    // 제자리 회전
        float _lift;   // 위로 뜬 높이
        Vector3 _squash = Vector3.one;

        Quaternion _baseRotation = Quaternion.identity;

        void Awake() => _phase = Random.value * 10f;

        void Update()
        {
            float t = Time.time * 2.2f + _phase;
            if (Body != null) Body.localScale = new Vector3(1f, 1f + Mathf.Sin(t) * 0.025f, 1f); // 숨쉬기
            _droopBlend = Mathf.MoveTowards(_droopBlend, _droop ? 1f : 0f, Time.deltaTime * 3f);
            if (Head != null)
                Head.localRotation = Quaternion.Euler(Mathf.Lerp(Mathf.Sin(t * 0.5f) * 3f, 28f, _droopBlend), 0f, Mathf.Sin(t * 0.35f) * 2.5f);
            if (_hopping) return;

            // 포기하면 몸 전체가 앞으로 처지고 조금 작아진다(모델은 머리만 숙일 수 없어서)
            float sway = Mathf.Sin(t * 0.35f) * 1.5f;
            transform.position = Seat + Vector3.up * _lift;
            transform.localRotation = _baseRotation * Quaternion.Euler(_lean + _droopBlend * 16f, _yaw, _roll + sway);
            float shrink = 1f - _droopBlend * 0.07f;
            transform.localScale = new Vector3(_squash.x * shrink, _squash.y * shrink, _squash.z * shrink);
        }

        /// <summary>손 위치(주사위·컵이 출발하는 곳).</summary>
        public Vector3 HandPosition => ArmR.position + transform.forward * 0.45f + Vector3.up * 0.35f;

        public void FaceTowards(Vector3 point)
        {
            Vector3 dir = point - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);
        }

        /// <summary>지금 바라보는 방향을 기본 자세로 기억한다.</summary>
        public void RememberFacing() => _baseRotation = transform.localRotation;

        /// <summary>고개를 떨군다(포기했을 때). 다음 판에 false로 되돌린다.</summary>
        public void SetDroop(bool droop) => _droop = droop;

        /// <summary>몸짓 하나를 한다. 하던 몸짓은 멈추고 자세를 되돌린 뒤 시작한다.</summary>
        public void React(Reaction r)
        {
            if (!isActiveAndEnabled) return;
            if (_reaction != null) StopCoroutine(_reaction);
            ResetPose();
            _reaction = StartCoroutine(Run(r));
        }

        void ResetPose()
        {
            _lean = _roll = _yaw = _lift = 0f;
            _squash = Vector3.one;
            if (ArmR != null) ArmR.localRotation = Quaternion.identity;
        }

        IEnumerator Run(Reaction r)
        {
            switch (r)
            {
                case Reaction.Nod:
                    for (int i = 0; i < 2; i++) yield return Curve(0.22f, k => _lean = Mathf.Sin(k * Mathf.PI) * 14f);
                    break;
                case Reaction.Bounce:
                    yield return Curve(0.34f, k =>
                    {
                        float h = Mathf.Sin(k * Mathf.PI);
                        _lift = h * 0.5f;
                        _squash = new Vector3(1f - h * 0.06f, 1f + h * 0.1f, 1f - h * 0.06f);
                    });
                    break;
                case Reaction.Wink:
                    yield return Curve(0.18f, k => { _roll = k * 16f; _lift = k * 0.18f; });
                    yield return new WaitForSeconds(0.35f);
                    yield return Curve(0.25f, k => { _roll = (1f - k) * 16f; _lift = (1f - k) * 0.18f; });
                    break;
                case Reaction.Yawn:
                    // 뒤로 젖히며 쭉 늘어났다가, 꾸벅
                    yield return Curve(0.5f, k => { _lean = -10f * k; _squash = new Vector3(1f - 0.06f * k, 1f + 0.13f * k, 1f - 0.06f * k); });
                    yield return new WaitForSeconds(0.4f);
                    yield return Curve(0.45f, k => { _lean = Mathf.Lerp(-10f, 8f, k); _squash = Vector3.Lerp(new Vector3(0.94f, 1.13f, 0.94f), Vector3.one, k); });
                    yield return Curve(0.3f, k => _lean = 8f * (1f - k));
                    break;
                case Reaction.Slump:
                    yield return Curve(0.15f, k => _squash = new Vector3(1f + 0.08f * k, 1f - 0.14f * k, 1f + 0.08f * k));
                    yield return Curve(0.35f, k => _squash = Vector3.Lerp(new Vector3(1.08f, 0.86f, 1.08f), Vector3.one, k));
                    break;
                case Reaction.Cheer:
                    for (int i = 0; i < 3; i++)
                    {
                        int jump = i;
                        yield return Curve(0.32f, k =>
                        {
                            float h = Mathf.Sin(k * Mathf.PI);
                            _lift = h * 0.8f;
                            _squash = new Vector3(1f - h * 0.05f, 1f + h * 0.08f, 1f - h * 0.05f);
                            if (jump == 1) _yaw = k * 360f; // 가운데 점프에서 한 바퀴
                            if (ArmR != null) ArmR.localRotation = Quaternion.Euler(-150f * h, 0f, 0f);
                        });
                    }
                    _yaw = 0f;
                    break;
                case Reaction.Sob:
                    yield return Curve(1.5f, k =>
                    {
                        _roll = Mathf.Sin(k * 1.5f * 14f * Mathf.PI * 2f) * 7f * (1f - k * 0.5f);
                        _lift = Mathf.Abs(Mathf.Sin(k * 1.5f * 5f * Mathf.PI)) * 0.12f;
                        _squash = new Vector3(1.05f, 0.92f, 1.05f);
                    });
                    break;
                case Reaction.Shrug:
                    yield return Curve(0.2f, k => { _lift = k * 0.15f; _roll = Mathf.Sin(k * Mathf.PI) * 6f; });
                    yield return Curve(0.3f, k => _lift = (1f - k) * 0.15f);
                    break;
                case Reaction.Surprise:
                    yield return Curve(0.16f, k => { _lift = k * 0.55f; _squash = new Vector3(1f - 0.08f * k, 1f + 0.18f * k, 1f - 0.08f * k); });
                    yield return new WaitForSeconds(0.2f);
                    yield return Curve(0.3f, k => { _lift = (1f - k) * 0.55f; _squash = Vector3.Lerp(new Vector3(0.92f, 1.18f, 0.92f), Vector3.one, k); });
                    break;
                case Reaction.Wave:
                    yield return Curve(0.9f, k => _roll = Mathf.Sin(k * 2f * Mathf.PI * 2f) * 9f);
                    break;
            }
            ResetPose();
            _reaction = null;
        }

        /// <summary>duration 동안 0→1 진행도를 apply에 넘긴다.</summary>
        static IEnumerator Curve(float duration, System.Action<float> apply)
        {
            for (float e = 0; e < duration; e += Time.deltaTime)
            {
                apply(e / duration);
                yield return null;
            }
            apply(1f);
        }

        /// <summary>깡충깡충 뛰어 자리로 나온다.</summary>
        public IEnumerator HopTo(Vector3 target, int hops, float duration)
        {
            _hopping = true;
            Vector3 from = transform.position;
            for (float e = 0; e < duration; e += Time.deltaTime)
            {
                float k = e / duration;
                float hop = Mathf.Abs(Mathf.Sin(k * Mathf.PI * hops));
                transform.position = Vector3.Lerp(from, target, Fx.Ease(k)) + Vector3.up * (hop * 0.7f);
                transform.localScale = new Vector3(1f + (1f - hop) * 0.06f, 1f - (1f - hop) * 0.08f, 1f + (1f - hop) * 0.06f);
                yield return null;
            }
            transform.position = target;
            transform.localScale = Vector3.one;
            _hopping = false;
        }

        /// <summary>
        /// 등장 전에 자리 밖으로 옮겨 둔다. HopTo가 시작되기 전 프레임에 Update가 자리로 끌어오지 않게 한다.
        /// </summary>
        public void PlaceAway(Vector3 position)
        {
            _hopping = true;
            transform.position = position;
        }

        /// <summary>몸을 뒤로 젖혔다가 앞으로 휘두른다(던지기). 도형일 때는 팔도 함께 돈다.</summary>
        public IEnumerator Throw()
        {
            yield return Swing(0f, -70f, 0.16f);
            yield return Swing(-70f, 80f, 0.12f);
            yield return Swing(80f, 0f, 0.25f);
        }

        IEnumerator Swing(float from, float to, float duration)
        {
            for (float e = 0; e < duration; e += Time.deltaTime)
            {
                float a = Mathf.Lerp(from, to, Fx.Ease(e / duration));
                if (ArmR != null) ArmR.localRotation = Quaternion.Euler(a, 0f, 0f);
                _lean = a * 0.15f;
                yield return null;
            }
            if (ArmR != null) ArmR.localRotation = Quaternion.Euler(to, 0f, 0f);
            _lean = to * 0.15f;
        }

        /// <summary>승리: 예전 호출 모양을 위해 남겨 둔다.</summary>
        public IEnumerator Cheer()
        {
            React(Reaction.Cheer);
            while (_reaction != null) yield return null;
        }

        /// <summary>올리기 때의 짧은 으쓱.</summary>
        public IEnumerator Bounce()
        {
            React(Reaction.Bounce);
            while (_reaction != null) yield return null;
        }
    }
}
