using UnityEngine;

namespace DiceHeist.Game
{
    public enum AnimalKind
    {
        Rabbit,
        Raccoon,
        Cat,
        // 아래 셋은 싱글 단계의 보스·딜러용(아직 자리에 쓰지 않음). 3D 모델만 있고, 모델이 없으면 고양이 모양 도형으로 나온다
        Fox,
        Bear,
        Penguin,
    }

    /// <summary>기본 도형을 조립한 2~3등신 동물. 나중에 모델로 바꿀 때는 이 클래스만 갈아 끼우면 된다.</summary>
    public static class AnimalBuilder
    {
        static readonly Color Black = new Color(0.12f, 0.13f, 0.14f);
        static readonly Color White = new Color(0.98f, 0.97f, 0.94f);
        static readonly Color Pink = new Color(0.96f, 0.68f, 0.72f);

        public static AnimalActor Build(AnimalKind kind, Transform parent)
        {
            var root = new GameObject(kind.ToString());
            root.transform.SetParent(parent, false);
            var actor = root.AddComponent<AnimalActor>();
            actor.Kind = kind;

            Color fur, belly;
            switch (kind)
            {
                case AnimalKind.Rabbit: fur = new Color(0.97f, 0.94f, 0.88f); belly = White; break;
                case AnimalKind.Raccoon: fur = new Color(0.56f, 0.53f, 0.50f); belly = new Color(0.80f, 0.77f, 0.72f); break;
                default: fur = new Color(0.45f, 0.50f, 0.62f); belly = new Color(0.86f, 0.88f, 0.93f); break;
            }

            // 몸통
            actor.Body = Fx.Pivot("Body", root.transform, Vector3.zero);
            Fx.Prim(PrimitiveType.Sphere, actor.Body, new Vector3(0, 0.72f, 0), new Vector3(1.25f, 1.3f, 1.1f), fur, name: "Torso");
            Fx.Prim(PrimitiveType.Sphere, actor.Body, new Vector3(0, 0.66f, 0.2f), new Vector3(0.9f, 0.95f, 0.8f), belly, name: "Belly");
            Fx.Prim(PrimitiveType.Sphere, actor.Body, new Vector3(-0.32f, 0.1f, 0.15f), new Vector3(0.42f, 0.25f, 0.6f), fur, name: "FootL");
            Fx.Prim(PrimitiveType.Sphere, actor.Body, new Vector3(0.32f, 0.1f, 0.15f), new Vector3(0.42f, 0.25f, 0.6f), fur, name: "FootR");

            // 팔: 어깨를 축으로 휘두른다
            var armL = Fx.Pivot("ArmL", actor.Body, new Vector3(-0.62f, 1.1f, 0.1f));
            Fx.Prim(PrimitiveType.Sphere, armL, new Vector3(-0.05f, -0.22f, 0.05f), new Vector3(0.32f, 0.55f, 0.32f), fur);
            actor.ArmR = Fx.Pivot("ArmR", actor.Body, new Vector3(0.62f, 1.1f, 0.1f));
            Fx.Prim(PrimitiveType.Sphere, actor.ArmR, new Vector3(0.05f, -0.22f, 0.05f), new Vector3(0.32f, 0.55f, 0.32f), fur);

            // 머리
            actor.Head = Fx.Pivot("Head", root.transform, new Vector3(0, 1.78f, 0.05f));
            var h = actor.Head;
            Fx.Prim(PrimitiveType.Sphere, h, Vector3.zero, new Vector3(1.35f, 1.2f, 1.2f), fur, name: "Skull");
            Fx.Prim(PrimitiveType.Sphere, h, new Vector3(-0.42f, -0.14f, 0.44f), new Vector3(0.22f, 0.13f, 0.1f), Pink, name: "CheekL");
            Fx.Prim(PrimitiveType.Sphere, h, new Vector3(0.42f, -0.14f, 0.44f), new Vector3(0.22f, 0.13f, 0.1f), Pink, name: "CheekR");

            switch (kind)
            {
                case AnimalKind.Rabbit: Rabbit(actor, fur); break;
                case AnimalKind.Raccoon: Raccoon(actor, fur); break;
                default: Cat(actor, fur); break;
            }

            // VARCO로 만든 3D 모델(Assets/Art/Characters/3D/Resources/Characters3D, glTFast로 가져옴)이 있으면 도형 대신 그것을 보인다.
            // 피벗(Body·Head·ArmR)은 연출(숨쉬기·던지기·처지기)과 손 위치 계산에 그대로 쓰고, 모델은 Body에 붙여 함께 움직인다
            var model = Resources.Load<GameObject>(ModelFolder + "/" + kind.ToString().ToLowerInvariant());
            if (model != null) UseModel(actor, model);
            return actor;
        }

        /// <summary>Resources 안에서 3D 모델이 있는 폴더.</summary>
        public const string ModelFolder = "Characters3D";
        /// <summary>도형 동물과 같은 키(머리 꼭대기·귀 끝까지).</summary>
        const float ModelHeight = 2.4f;

        static void UseModel(AnimalActor actor, GameObject prefab)
        {
            foreach (var r in actor.GetComponentsInChildren<Renderer>()) r.enabled = false;

            var inst = Object.Instantiate(prefab, actor.Body, false);
            inst.name = "Model";
            var renderers = inst.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            // Body 기준 좌표로 모델 크기를 재서 바닥을 0에, 가운데를 몸 중심에 맞추고 키를 맞춘다
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var r in renderers)
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var local = actor.Body.InverseTransformPoint(corner);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
            }
            float height = max.y - min.y;
            if (height <= 0.0001f) return;
            float k = ModelHeight / height;
            inst.transform.localScale *= k;
            var center = (min + max) * 0.5f;
            inst.transform.localPosition -= new Vector3(center.x, min.y, center.z) * k;
        }

        static void Eyes(Transform h, float x, float z, Color iris, float size)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                Fx.Prim(PrimitiveType.Sphere, h, new Vector3(s * x, 0.08f, z), Vector3.one * size, iris, name: "Eye");
                Fx.Prim(PrimitiveType.Sphere, h, new Vector3(s * x + 0.03f, 0.12f, z + size * 0.38f), Vector3.one * size * 0.35f, White, name: "Glint");
            }
        }

        static void Rabbit(AnimalActor a, Color fur)
        {
            var h = a.Head;
            Eyes(h, 0.28f, 0.52f, Black, 0.15f);
            Fx.Prim(PrimitiveType.Sphere, h, new Vector3(0, -0.08f, 0.6f), new Vector3(0.13f, 0.09f, 0.09f), Pink, name: "Nose");
            for (int s = -1; s <= 1; s += 2)
            {
                var ear = Fx.Pivot("Ear", h, new Vector3(s * 0.26f, 0.45f, -0.05f));
                ear.localRotation = Quaternion.Euler(-6f, 0f, -s * 9f);
                Fx.Prim(PrimitiveType.Capsule, ear, new Vector3(0, 0.6f, 0), new Vector3(0.3f, 0.62f, 0.2f), fur);
                Fx.Prim(PrimitiveType.Capsule, ear, new Vector3(0, 0.6f, 0.07f), new Vector3(0.16f, 0.48f, 0.1f), Pink);
            }
            Fx.Prim(PrimitiveType.Sphere, a.Body, new Vector3(0, 0.45f, -0.58f), Vector3.one * 0.34f, White, name: "Tail");
        }

        static void Raccoon(AnimalActor a, Color fur)
        {
            var h = a.Head;
            var dark = new Color(0.20f, 0.19f, 0.21f);
            for (int s = -1; s <= 1; s += 2)
            {
                Fx.Prim(PrimitiveType.Sphere, h, new Vector3(s * 0.3f, 0.08f, 0.45f), new Vector3(0.46f, 0.3f, 0.2f), dark,
                    Quaternion.Euler(0, 0, s * 14f), "Mask");
                Fx.Prim(PrimitiveType.Sphere, h, new Vector3(s * 0.47f, 0.52f, 0f), new Vector3(0.36f, 0.36f, 0.2f), dark, name: "Ear");
            }
            Eyes(h, 0.29f, 0.55f, White, 0.15f);
            for (int s = -1; s <= 1; s += 2)
                Fx.Prim(PrimitiveType.Sphere, h, new Vector3(s * 0.29f, 0.08f, 0.6f), Vector3.one * 0.08f, Black, name: "Pupil");
            Fx.Prim(PrimitiveType.Sphere, h, new Vector3(0, -0.17f, 0.48f), new Vector3(0.5f, 0.34f, 0.32f), White, name: "Muzzle");
            Fx.Prim(PrimitiveType.Sphere, h, new Vector3(0, -0.1f, 0.65f), new Vector3(0.14f, 0.1f, 0.1f), Black, name: "Nose");
            // 줄무늬 꼬리
            for (int i = 0; i < 6; i++)
            {
                float k = i / 5f;
                Fx.Prim(PrimitiveType.Sphere, a.Body, new Vector3(0, 0.3f + k * 0.75f, -0.6f - Mathf.Sin(k * Mathf.PI) * 0.35f),
                    Vector3.one * (0.34f - k * 0.06f), i % 2 == 0 ? fur : dark, name: "Tail");
            }
        }

        static void Cat(AnimalActor a, Color fur)
        {
            var h = a.Head;
            Eyes(h, 0.28f, 0.52f, new Color(0.80f, 0.86f, 0.35f), 0.16f);
            for (int s = -1; s <= 1; s += 2)
            {
                Fx.Prim(PrimitiveType.Sphere, h, new Vector3(s * 0.28f, 0.08f, 0.585f), new Vector3(0.035f, 0.13f, 0.04f), Black, name: "Slit");
                // 마름모로 돌린 상자의 윗부분만 머리 밖으로 나와 세모 귀가 된다
                Fx.Prim(PrimitiveType.Cube, h, new Vector3(s * 0.4f, 0.45f, 0f), new Vector3(0.42f, 0.42f, 0.16f), fur,
                    Quaternion.Euler(0, 0, 45f), "Ear");
                Fx.Prim(PrimitiveType.Cube, h, new Vector3(s * 0.4f, 0.45f, 0.05f), new Vector3(0.26f, 0.26f, 0.1f), Pink,
                    Quaternion.Euler(0, 0, 45f), "EarInner");
                for (int w = -1; w <= 1; w++)
                    Fx.Prim(PrimitiveType.Cube, h, new Vector3(s * 0.55f, -0.12f + w * 0.07f, 0.4f), new Vector3(0.36f, 0.015f, 0.015f), White,
                        Quaternion.Euler(0, s * -20f, s * w * 10f), "Whisker");
            }
            Fx.Prim(PrimitiveType.Sphere, h, new Vector3(0, -0.08f, 0.6f), new Vector3(0.1f, 0.07f, 0.07f), Pink, name: "Nose");
            // 위로 말아 올린 꼬리
            for (int i = 0; i < 8; i++)
            {
                float k = i / 7f;
                Fx.Prim(PrimitiveType.Sphere, a.Body,
                    new Vector3(Mathf.Sin(k * 2.2f) * 0.25f, 0.25f + k * 1.1f, -0.55f - Mathf.Sin(k * Mathf.PI) * 0.3f),
                    Vector3.one * 0.2f, fur, name: "Tail");
            }
        }
    }
}
