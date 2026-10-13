using System.Collections;
using UnityEngine;

namespace DiceHeist.Game
{
    /// <summary>동물 도박사 하나의 등장·던지기·리액션 연출. 승패에는 영향이 없다.</summary>
    public sealed class AnimalActor : MonoBehaviour
    {
        public Transform Head;
        public Transform ArmR;
        public Transform Body;
        /// <summary>자리에 섰을 때의 위치.</summary>
        public Vector3 Seat;

        float _phase;
        bool _droop;
        float _droopBlend;

        void Awake() => _phase = Random.value * 10f;

        void Update()
        {
            // 숨쉬기와 고개 까딱임
            float t = Time.time * 2.2f + _phase;
            if (Body != null) Body.localScale = new Vector3(1f, 1f + Mathf.Sin(t) * 0.025f, 1f);
            _droopBlend = Mathf.MoveTowards(_droopBlend, _droop ? 1f : 0f, Time.deltaTime * 3f);
            if (Head != null)
                Head.localRotation = Quaternion.Euler(Mathf.Lerp(Mathf.Sin(t * 0.5f) * 3f, 28f, _droopBlend), 0f, Mathf.Sin(t * 0.35f) * 2.5f);
        }

        /// <summary>손 위치(주사위·컵이 출발하는 곳).</summary>
        public Vector3 HandPosition => ArmR.position + transform.forward * 0.45f + Vector3.up * 0.35f;

        public void FaceTowards(Vector3 point)
        {
            Vector3 dir = point - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);
        }

        /// <summary>고개를 떨군다(포기했을 때).</summary>
        public void SetDroop(bool droop) => _droop = droop;

        /// <summary>깡충깡충 뛰어 자리로 나온다.</summary>
        public IEnumerator HopTo(Vector3 target, int hops, float duration)
        {
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
        }

        /// <summary>팔을 뒤로 젖혔다가 앞으로 휘두른다.</summary>
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
                float k = Fx.Ease(e / duration);
                ArmR.localRotation = Quaternion.Euler(Mathf.Lerp(from, to, k), 0f, 0f);
                transform.localRotation = _baseRotation * Quaternion.Euler(Mathf.Lerp(from, to, k) * 0.12f, 0f, 0f);
                yield return null;
            }
            ArmR.localRotation = Quaternion.Euler(to, 0f, 0f);
            transform.localRotation = _baseRotation * Quaternion.Euler(to * 0.12f, 0f, 0f);
        }

        Quaternion _baseRotation = Quaternion.identity;

        /// <summary>지금 바라보는 방향을 기본 자세로 기억한다.</summary>
        public void RememberFacing() => _baseRotation = transform.localRotation;

        public IEnumerator Cheer()
        {
            for (int i = 0; i < 3; i++)
            {
                for (float e = 0; e < 0.32f; e += Time.deltaTime)
                {
                    float k = e / 0.32f;
                    transform.position = Seat + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.8f);
                    ArmR.localRotation = Quaternion.Euler(-150f * Mathf.Sin(k * Mathf.PI), 0f, 0f);
                    yield return null;
                }
            }
            transform.position = Seat;
            ArmR.localRotation = Quaternion.identity;
        }

        /// <summary>올리기 때의 짧은 으쓱.</summary>
        public IEnumerator Bounce()
        {
            for (float e = 0; e < 0.3f; e += Time.deltaTime)
            {
                transform.position = Seat + Vector3.up * (Mathf.Sin(e / 0.3f * Mathf.PI) * 0.45f);
                yield return null;
            }
            transform.position = Seat;
        }
    }
}
