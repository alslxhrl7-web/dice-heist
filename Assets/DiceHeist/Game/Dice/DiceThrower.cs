using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DiceHeist.Game
{
    /// <summary>
    /// 주사위 굴림 연출. 물리를 미리 끝까지 돌려 궤적을 녹화한 뒤, 멈춘 윗면에 로직이 정한 눈을 입히고 재생한다.
    /// 결과는 연출과 무관하게 이미 정해져 있다(나중에 서버가 정한 눈을 그대로 받아 재생하면 된다).
    /// 연출용 난수는 UnityEngine.Random을 쓰며 게임 난수(IRandom)를 소비하지 않는다.
    /// </summary>
    public static class DiceThrower
    {
        const float Step = 0.02f;
        const int MaxSteps = 350;
        const int MaxAttempts = 8;

        struct Frame
        {
            public Vector3[] Pos;
            public Quaternion[] Rot;
            /// <summary>이 프레임에 일어난 충돌의 세기(0~1). 없으면 null. 재생할 때 소리를 낸다.</summary>
            public List<float> Impacts;
            /// <summary>바닥을 구르는 정도(0~1). 구르는 소리의 크기</summary>
            public float Roll;
        }

        /// <summary>속도가 한 스텝에 이만큼 넘게 바뀌면 부딪힌 것으로 본다(중력만으로는 한 스텝 0.2).</summary>
        const float ImpactThreshold = 1.1f;
        /// <summary>한 주사위가 이보다 짧은 간격으로 또 부딪히면 소리를 내지 않는다(구를 때 다다닥 겹치지 않게).</summary>
        const float ImpactGap = 0.06f;

        /// <param name="dice">굴릴 주사위</param>
        /// <param name="values">각 주사위에 나와야 하는 눈</param>
        /// <param name="origin">던지는 지점</param>
        /// <param name="direction">던지는 방향(수평)</param>
        public static IEnumerator Throw(IList<DieView> dice, IList<int> values, Vector3 origin, Vector3 direction)
        {
            List<Frame> frames = null;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                frames = Simulate(dice, origin, direction.normalized);
                if (SettledFlat(dice)) break;
            }

            for (int i = 0; i < dice.Count; i++)
            {
                dice[i].ShowValueOnFace(values[i], dice[i].UpFace());
                dice[i].Body.isKinematic = true;
            }

            float t = 0f;
            int last = frames.Count - 1;
            int played = -1;

            // 녹음한 쏟기 소리가 있으면 충돌마다 딸깍 대신 그것을 한 번 튼다. 녹음의 첫 부딪힘을 물리의 첫 충돌에 맞춘다
            bool recorded = DiceAudio.HasPour;
            float pourAt = 0f;
            if (recorded)
            {
                int first = frames.FindIndex(fr => fr.Impacts != null);
                pourAt = Mathf.Max(0f, (first < 0 ? 0 : first) * Step - DiceAudio.PourFirstHit);
            }
            bool poured = false;

            while (true)
            {
                int f = Mathf.Min(last, Mathf.FloorToInt(t / Step));
                if (recorded)
                {
                    if (!poured && t >= pourAt)
                    {
                        DiceAudio.Pour(0.55f + 0.45f * dice.Count / 5f); // 적게 굴리면 조금 작게
                        poured = true;
                    }
                }
                else
                {
                    for (int k = played + 1; k <= f; k++)
                        if (frames[k].Impacts != null)
                            foreach (float s in frames[k].Impacts) DiceAudio.Impact(s);
                    DiceAudio.SetRoll(frames[f].Roll);
                }
                played = f;
                for (int i = 0; i < dice.Count; i++)
                    dice[i].transform.SetPositionAndRotation(frames[f].Pos[i], frames[f].Rot[i]);
                if (f == last) break;
                yield return null;
                t += Time.deltaTime;
            }
            DiceAudio.SetRoll(0f);
            if (recorded) DiceAudio.EndPour(); // 다 멈췄는데 녹음이 아직 구르면 짧게 잦아들게
            Physics.SyncTransforms();
        }

        static List<Frame> Simulate(IList<DieView> dice, Vector3 origin, Vector3 dir)
        {
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            for (int i = 0; i < dice.Count; i++)
            {
                var d = dice[i];
                Vector3 pos = origin + Vector3.up * (0.25f * i) + side * Random.Range(-0.5f, 0.5f) + dir * Random.Range(-0.1f, 0.2f);
                d.transform.SetPositionAndRotation(pos, Random.rotationUniform);
                d.Body.isKinematic = false;
            }
            Physics.SyncTransforms();
            foreach (var d in dice)
            {
                d.Body.linearVelocity = dir * Random.Range(3.5f, 5.5f) + side * Random.Range(-1.2f, 1.2f) + Vector3.up * Random.Range(0f, 1.2f);
                d.Body.angularVelocity = Random.onUnitSphere * Random.Range(12f, 26f);
            }

            var frames = new List<Frame>(MaxSteps);
            var prevVel = new Vector3[dice.Count];
            var lastHit = new float[dice.Count];
            for (int i = 0; i < dice.Count; i++)
            {
                prevVel[i] = dice[i].Body.linearVelocity;
                lastHit[i] = -1f;
            }
            for (int s = 0; s < MaxSteps; s++)
            {
                Physics.Simulate(Step);
                var fr = new Frame { Pos = new Vector3[dice.Count], Rot = new Quaternion[dice.Count] };
                bool asleep = true;
                float now = (s + 1) * Step;
                for (int i = 0; i < dice.Count; i++)
                {
                    fr.Pos[i] = dice[i].Body.position;
                    fr.Rot[i] = dice[i].Body.rotation;
                    if (!dice[i].Body.IsSleeping()) asleep = false;

                    // 충돌 찾기: 속도가 갑자기 바뀐 순간
                    Vector3 v = dice[i].Body.linearVelocity;
                    float dv = (v - prevVel[i]).magnitude;
                    prevVel[i] = v;
                    if (dv > ImpactThreshold && now - lastHit[i] > ImpactGap)
                    {
                        lastHit[i] = now;
                        (fr.Impacts ??= new List<float>()).Add(Mathf.Clamp01(dv / 6f));
                    }
                    // 바닥에 닿아 있으면서 움직이는 주사위만 "구른다"
                    if (fr.Pos[i].y < dice[i].Size * 0.8f)
                        fr.Roll += Mathf.Clamp01((v.magnitude + dice[i].Body.angularVelocity.magnitude * 0.08f) / 3f) / dice.Count * 1.6f;
                }
                frames.Add(fr);
                if (asleep && s > 10) break;
            }

            // 다음 호출의 UpFace가 최종 자세를 보도록 트랜스폼에 반영
            var lastFrame = frames[frames.Count - 1];
            for (int i = 0; i < dice.Count; i++)
                dice[i].transform.SetPositionAndRotation(lastFrame.Pos[i], lastFrame.Rot[i]);
            return frames;
        }

        /// <summary>모두 바닥에 평평하게 멈췄는지(기울거나 겹쳐 올라가지 않았는지).</summary>
        static bool SettledFlat(IList<DieView> dice)
        {
            foreach (var d in dice)
            {
                var up = Quaternion.Inverse(d.transform.rotation) * Vector3.up;
                float flat = Mathf.Max(Mathf.Abs(up.x), Mathf.Max(Mathf.Abs(up.y), Mathf.Abs(up.z)));
                if (flat < 0.995f) return false;
                if (Mathf.Abs(d.transform.position.y - d.Size * 0.5f) > 0.05f) return false;
            }
            return true;
        }
    }
}
