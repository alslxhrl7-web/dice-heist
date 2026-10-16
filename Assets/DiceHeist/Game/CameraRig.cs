using System.Collections;
using UnityEngine;

namespace DiceHeist.Game
{
    /// <summary>카메라 구도. 단계마다 보기 좋은 곳으로 부드럽게 옮겨 간다.</summary>
    public enum CameraShot
    {
        /// <summary>테이블 전체를 위에서(등장, 판 사이)</summary>
        Overview,
        /// <summary>내 주사위를 내려다보기(굴리기·남기기). 눈이 잘 읽히게</summary>
        Dice,
        /// <summary>토끼 뒤 3인칭으로 상대를 마주 보기(상대 굴림·베팅·쇼다운). 상대 얼굴과 리액션이 크게 보이게</summary>
        Table,
    }

    /// <summary>
    /// 카메라를 구도 사이로 옮긴다. 위치와 바라보는 점을 함께 부드럽게(0.6초) 옮기고, 옮기는 중에 다른 구도가 오면 그쪽으로 바로 꺾는다.
    /// 화면의 이름표·말풍선은 HudView가 매 프레임 3D 위치를 따라가므로 카메라가 움직여도 붙어 다닌다.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        struct Pose
        {
            public Vector3 Position;
            public Vector3 LookAt;
            public float Fov;
        }

        static Pose Of(CameraShot shot)
        {
            switch (shot)
            {
                case CameraShot.Dice:
                    // 내 굴림 칸과 주사위 줄을 거의 위에서
                    return new Pose { Position = new Vector3(0.4f, 7.6f, -7.4f), LookAt = new Vector3(0.4f, 0f, -2.3f), Fov = 40f };
                case CameraShot.Table:
                    // 토끼 뒤쪽 낮은 자리에서 건너편 상대들을 마주 본다. 앞쪽에 내 주사위 줄이 걸린다
                    return new Pose { Position = new Vector3(-0.6f, 7.4f, -10.6f), LookAt = new Vector3(0.3f, 0f, 0.2f), Fov = 46f };
                default:
                    return new Pose { Position = new Vector3(0f, 12.5f, -11.5f), LookAt = new Vector3(0f, 0.3f, -1.2f), Fov = 42f };
            }
        }

        Camera _cam;
        Coroutine _move;
        Vector3 _look;

        public CameraShot Current { get; private set; } = CameraShot.Overview;

        public static CameraRig Attach(Camera cam)
        {
            var rig = cam.GetComponent<CameraRig>() ?? cam.gameObject.AddComponent<CameraRig>();
            rig._cam = cam;
            rig.Snap(CameraShot.Overview);
            return rig;
        }

        /// <summary>곧바로 그 구도로.</summary>
        public void Snap(CameraShot shot)
        {
            if (_move != null) StopCoroutine(_move);
            var p = Of(shot);
            _look = p.LookAt;
            _cam.transform.position = p.Position;
            _cam.transform.LookAt(_look);
            _cam.fieldOfView = p.Fov;
            Current = shot;
        }

        /// <summary>그 구도로 부드럽게 옮긴다. 이미 그 구도면 아무것도 안 한다.</summary>
        public void Go(CameraShot shot, float duration = 0.6f)
        {
            if (shot == Current && _move == null) return;
            if (_move != null) StopCoroutine(_move);
            Current = shot;
            _move = StartCoroutine(Move(Of(shot), duration));
        }

        IEnumerator Move(Pose to, float duration)
        {
            Vector3 p0 = _cam.transform.position, l0 = _look;
            float f0 = _cam.fieldOfView;
            for (float e = 0; e < duration; e += Time.deltaTime)
            {
                float k = Fx.Ease(e / duration);
                _look = Vector3.Lerp(l0, to.LookAt, k);
                _cam.transform.position = Vector3.Lerp(p0, to.Position, k);
                _cam.transform.LookAt(_look);
                _cam.fieldOfView = Mathf.Lerp(f0, to.Fov, k);
                yield return null;
            }
            _look = to.LookAt;
            _cam.transform.position = to.Position;
            _cam.transform.LookAt(_look);
            _cam.fieldOfView = to.Fov;
            _move = null;
        }
    }
}
