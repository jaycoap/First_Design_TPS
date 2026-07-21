using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// TPS 오버숄더 카메라. 마우스로 궤도 회전하고, 조준(우클릭) 시 어깨 너머로 붙으며 FOV가 좁아진다.
/// 카메라 오브젝트(Main Camera)에 붙이고 target에 플레이어를 지정한다.
/// 벽 뚫림 방지를 위한 충돌(clipping) 처리 포함.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("추적 대상")]
    [Tooltip("따라다닐 플레이어 Transform")]
    [SerializeField] private Transform target;

    [Tooltip("타겟 기준 카메라가 바라보는 지점의 높이(어깨/머리 근처)")]
    [SerializeField] private float pivotHeight = 1.5f;

    [Header("궤도(Orbit) 설정")]
    [SerializeField] private float mouseSensitivity = 0.15f;
    [SerializeField] private float minPitch = -35f;
    [SerializeField] private float maxPitch = 70f;
    [Tooltip("시작 시 카메라 상하 각도(양수 = 살짝 내려다봄). 탑뷰 방지용 초기값.")]
    [SerializeField] private float initialPitch = 12f;

    [Header("일반 상태")]
    [Tooltip("타겟에서 카메라까지 거리")]
    [SerializeField] private float normalDistance = 4f;
    [Tooltip("어깨 오프셋(오른쪽/위). 화면에서 캐릭터가 왼쪽에 오도록.")]
    [SerializeField] private Vector2 normalShoulder = new Vector2(0.6f, 0f);
    [SerializeField] private float normalFov = 60f;

    [Header("조준(Aim) 상태")]
    [SerializeField] private float aimDistance = 2f;
    [SerializeField] private Vector2 aimShoulder = new Vector2(0.8f, 0.1f);
    [SerializeField] private float aimFov = 40f;

    [Header("전환 부드러움")]
    [SerializeField] private float transitionSpeed = 12f;

    [Header("충돌(벽 뚫림 방지)")]
    [SerializeField] private LayerMask collisionMask = ~0;
    [SerializeField] private float collisionRadius = 0.2f;

    private float _yaw;
    private float _pitch;
    private float _currentDistance;
    private Vector2 _currentShoulder;
    private Camera _cam;
    private bool _isAiming;

    /// <summary>다른 스크립트(발사/이동)에서 현재 카메라 조준 여부와 방향을 참조.</summary>
    public bool IsAiming => _isAiming;
    public float Yaw => _yaw;

    private void Start()
    {
        _cam = GetComponent<Camera>();
        _currentDistance = normalDistance;
        _currentShoulder = normalShoulder;

        // 카메라의 현재 회전(탑뷰일 수 있음) 대신, 타겟 뒤·살짝 내려다보는 각도로 초기화
        _yaw = target != null ? target.eulerAngles.y : transform.eulerAngles.y;
        _pitch = initialPitch;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (Mouse.current == null) return;

        Vector2 delta = Mouse.current.delta.ReadValue() * mouseSensitivity;
        _yaw += delta.x;
        _pitch -= delta.y;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        // 우클릭 조준 상태 갱신
        _isAiming = Mouse.current.rightButton.isPressed;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // 상태별 목표값으로 부드럽게 보간
        float t = 1f - Mathf.Exp(-transitionSpeed * Time.deltaTime);
        float targetDist = _isAiming ? aimDistance : normalDistance;
        Vector2 targetShoulder = _isAiming ? aimShoulder : normalShoulder;
        float targetFov = _isAiming ? aimFov : normalFov;

        _currentDistance = Mathf.Lerp(_currentDistance, targetDist, t);
        _currentShoulder = Vector2.Lerp(_currentShoulder, targetShoulder, t);
        if (_cam != null)
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, targetFov, t);

        Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 pivot = target.position + Vector3.up * pivotHeight;

        // 어깨 오프셋을 카메라 회전 기준으로 적용
        Vector3 shoulderOffset = rotation * new Vector3(_currentShoulder.x, _currentShoulder.y, 0f);
        Vector3 pivotWithShoulder = pivot + shoulderOffset;

        Vector3 desiredPos = pivotWithShoulder - rotation * Vector3.forward * _currentDistance;

        // 벽 충돌 시 카메라를 앞으로 당김
        if (Physics.SphereCast(pivotWithShoulder, collisionRadius,
                (desiredPos - pivotWithShoulder).normalized,
                out RaycastHit hit, _currentDistance, collisionMask, QueryTriggerInteraction.Ignore))
        {
            desiredPos = pivotWithShoulder + (desiredPos - pivotWithShoulder).normalized * (hit.distance);
        }

        transform.position = desiredPos;
        transform.rotation = rotation;
    }
}
