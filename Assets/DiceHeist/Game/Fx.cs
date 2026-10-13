using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DiceHeist.Game
{
    /// <summary>프로토타입용 기본 도형·머티리얼·트윈 도우미.</summary>
    public static class Fx
    {
        static Material _base;
        static readonly Dictionary<Color, Material> Cache = new Dictionary<Color, Material>();

        public static Material Mat(Color c)
        {
            if (Cache.TryGetValue(c, out var m) && m != null) return m;
            if (_base == null)
            {
                // 현재 렌더 파이프라인의 기본 머티리얼을 복제해 쓴다
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _base = tmp.GetComponent<Renderer>().sharedMaterial;
                UnityEngine.Object.DestroyImmediate(tmp);
            }
            m = new Material(_base) { color = c };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.25f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
            Cache[c] = m;
            return m;
        }

        /// <summary>충돌체 없는 기본 도형.</summary>
        public static Transform Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Color color,
            Quaternion? localRot = null, string name = null)
        {
            var go = GameObject.CreatePrimitive(type);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            if (name != null) go.name = name;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = localRot ?? Quaternion.identity;
            t.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = Mat(color);
            return t;
        }

        public static Transform Pivot(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        public static float Ease(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>위치·회전을 부드럽게 옮긴다. arc만큼 위로 솟는 포물선을 그린다.</summary>
        public static IEnumerator MoveTo(Transform t, Vector3 pos, Quaternion rot, float duration, float arc = 0f)
        {
            Vector3 p0 = t.position;
            Quaternion r0 = t.rotation;
            for (float e = 0; e < duration; e += Time.deltaTime)
            {
                float k = Ease(e / duration);
                t.SetPositionAndRotation(
                    Vector3.Lerp(p0, pos, k) + Vector3.up * (arc * 4f * k * (1f - k)),
                    Quaternion.Slerp(r0, rot, k));
                yield return null;
            }
            t.SetPositionAndRotation(pos, rot);
        }

        public static IEnumerator ScaleTo(Transform t, Vector3 scale, float duration)
        {
            Vector3 s0 = t.localScale;
            for (float e = 0; e < duration; e += Time.deltaTime)
            {
                t.localScale = Vector3.Lerp(s0, scale, Ease(e / duration));
                yield return null;
            }
            t.localScale = scale;
        }

        /// <summary>여러 코루틴을 동시에 돌리고 모두 끝날 때까지 기다린다.</summary>
        public static IEnumerator All(MonoBehaviour host, params IEnumerator[] routines)
        {
            int left = routines.Length;
            foreach (var r in routines) host.StartCoroutine(Then(r, () => left--));
            while (left > 0) yield return null;
        }

        static IEnumerator Then(IEnumerator routine, Action done)
        {
            yield return routine;
            done();
        }
    }
}
