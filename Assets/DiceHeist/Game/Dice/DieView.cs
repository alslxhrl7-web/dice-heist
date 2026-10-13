using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DiceHeist.Game
{
    /// <summary>
    /// 3D 주사위 하나. 눈은 로직이 정하고, 이 클래스는 물리로 멈춘 윗면에 그 눈이 오도록 겉모습(Visual)만 돌려 맞춘다.
    /// </summary>
    public sealed class DieView : MonoBehaviour, IPointerClickHandler
    {
        // 겉모습 기준 각 눈의 면 방향. 마주 보는 면의 합은 7.
        static readonly Vector3[] FaceNormal =
        {
            Vector3.zero, Vector3.up, Vector3.forward, Vector3.right, Vector3.left, Vector3.back, Vector3.down,
        };

        // 3x3 칸 중 점이 찍히는 자리
        static readonly int[][] PipCells =
        {
            null,
            new[] { 4 },
            new[] { 0, 8 },
            new[] { 0, 4, 8 },
            new[] { 0, 2, 6, 8 },
            new[] { 0, 2, 4, 6, 8 },
            new[] { 0, 2, 3, 5, 6, 8 },
        };

        public int Index { get; private set; }
        public float Size { get; private set; }
        public Rigidbody Body { get; private set; }
        public bool Pickable;
        public event Action<DieView> Clicked;

        Transform _visual;
        Transform _ring;
        bool _held;

        public static DieView Create(string name, int index, float size, Color bodyColor, Color pipColor, bool physical)
        {
            var go = new GameObject(name);
            var die = go.AddComponent<DieView>();
            die.Index = index;
            die.Size = size;

            die._visual = Fx.Pivot("Visual", go.transform, Vector3.zero);
            Fx.Prim(PrimitiveType.Cube, die._visual, Vector3.zero, Vector3.one * size, bodyColor, name: "Body");
            for (int v = 1; v <= 6; v++) die.BuildPips(v, pipColor);

            die._ring = Fx.Prim(PrimitiveType.Cylinder, go.transform, Vector3.zero,
                new Vector3(size * 1.7f, 0.012f, size * 1.7f), new Color(0.89f, 0.64f, 0.15f), name: "HeldRing");
            die._ring.gameObject.SetActive(false);

            if (physical)
            {
                var col = go.AddComponent<BoxCollider>();
                col.size = Vector3.one * size;
                col.material = new PhysicsMaterial("Die") { bounciness = 0.35f, dynamicFriction = 0.45f, staticFriction = 0.5f };
                die.Body = go.AddComponent<Rigidbody>();
                die.Body.mass = 1f;
                die.Body.maxAngularVelocity = 60f;
                die.Body.isKinematic = true;
            }
            return die;
        }

        void BuildPips(int value, Color color)
        {
            Vector3 n = FaceNormal[value];
            Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
            Vector3 w = Vector3.Cross(n, u);
            float step = Size * 0.27f;
            var rot = Quaternion.LookRotation(n, u);
            foreach (int cell in PipCells[value])
            {
                int col = cell % 3 - 1, row = 1 - cell / 3;
                Vector3 pos = n * (Size * 0.5f) + u * (row * step) + w * (col * step);
                Fx.Prim(PrimitiveType.Sphere, _visual, pos, new Vector3(Size * 0.19f, Size * 0.19f, Size * 0.05f), color, rot, "Pip");
            }
        }

        /// <summary>from 방향 면을 to 방향 면으로 옮기는, 정육면체가 겹치는 회전.</summary>
        static Quaternion FaceToFace(Vector3 from, Vector3 to)
        {
            float d = Vector3.Dot(from, to);
            if (d > 0.5f) return Quaternion.identity;
            if (d < -0.5f) return Quaternion.AngleAxis(180f, Mathf.Abs(from.x) > 0.5f ? Vector3.up : Vector3.right);
            return Quaternion.AngleAxis(90f, Vector3.Cross(from, to));
        }

        /// <summary>지금 물리적으로 위를 향한 면(1~6).</summary>
        public int UpFace()
        {
            int best = 1;
            float bestDot = -2f;
            for (int f = 1; f <= 6; f++)
            {
                float d = Vector3.Dot(transform.rotation * FaceNormal[f], Vector3.up);
                if (d > bestDot) { bestDot = d; best = f; }
            }
            return best;
        }

        /// <summary>물리 면 face 자리에 value 눈이 보이도록 겉모습을 돌린다.</summary>
        public void ShowValueOnFace(int value, int face)
        {
            _visual.localRotation = FaceToFace(FaceNormal[value], FaceNormal[face]);
        }

        /// <summary>물리 면 face가 정확히 위를 보도록 바로 세운 회전.</summary>
        public static Quaternion Upright(int face) => FaceToFace(FaceNormal[face], Vector3.up);

        public bool Held
        {
            get => _held;
            set { _held = value; ApplyHeldPose(); }
        }

        /// <summary>고정된 주사위는 살짝 띄우고 바닥에 금색 원을 깐다. 본체 회전과 무관하게 항상 위쪽으로.</summary>
        public void ApplyHeldPose()
        {
            var inv = Quaternion.Inverse(transform.rotation);
            _visual.localPosition = _held ? inv * (Vector3.up * Size * 0.45f) : Vector3.zero;
            _ring.gameObject.SetActive(_held);
            _ring.localRotation = inv;
            _ring.localPosition = inv * (Vector3.down * (Size * 0.5f - 0.01f));
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Pickable) Clicked?.Invoke(this);
        }
    }
}
