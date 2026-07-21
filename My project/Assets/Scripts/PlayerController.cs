using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// TPS 플레이어 이동 컨트롤러(CharacterController 기반).
/// - 카메라 기준 방향으로 WASD 이동
/// - 평상시: 이동 방향으로 몸을 회전
/// - 조준 중: 카메라(=조준) 방향으로 몸을 고정 회전
/// - 중력/점프 처리
/// Animator가 지정되어 있으면 Speed / IsAiming 파라미터를 갱신한다(없어도 동작).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("TPS 카메라. 이동 방향/조준 상태의 기준이 된다.")]
    [SerializeField] private ThirdPersonCamera tpsCamera;
    [Tooltip("선택: 캐릭터 Animator. 없으면 애니메이션 없이 이동만 동작.")]
    [SerializeField] private Animator animator;

    [Header("이동")]
    [SerializeField] private float walkSpeed = 2.5f;
    [SerializeField] private float runSpeed = 5.5f;
    [SerializeField] private float aimSpeed = 2f;
    [SerializeField] private float rotationSpeed = 15f;

    [Header("중력/점프")]
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float jumpHeight = 1.2f;

    private CharacterController _cc;
    private Transform _camTransform;
    private float _verticalVelocity;

    // Animator 파라미터 해시(있을 때만 사용)
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsAimingHash = Animator.StringToHash("IsAiming");

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (tpsCamera == null) tpsCamera = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (tpsCamera != null) _camTransform = tpsCamera.transform;
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        bool isAiming = tpsCamera != null && tpsCamera.IsAiming;

        // --- 입력 읽기 (New Input System) ---
        Vector2 input = ReadMoveInput();
        bool running = Keyboard.current.leftShiftKey.isPressed;

        // 카메라 기준 이동 방향(수평 평면)
        Vector3 camForward = _camTransform != null ? _camTransform.forward : Vector3.forward;
        Vector3 camRight = _camTransform != null ? _camTransform.right : Vector3.right;
        camForward.y = 0f; camRight.y = 0f;
        camForward.Normalize(); camRight.Normalize();

        Vector3 moveDir = (camForward * input.y + camRight * input.x);
        float inputMag = Mathf.Clamp01(moveDir.magnitude);
        moveDir.Normalize();

        float speed = isAiming ? aimSpeed : (running ? runSpeed : walkSpeed);
        Vector3 horizontal = moveDir * speed * inputMag;

        // --- 회전 ---
        if (isAiming)
        {
            // 조준 중엔 카메라(요) 방향으로 몸을 정렬
            float yaw = tpsCamera.Yaw;
            Quaternion targetRot = Quaternion.Euler(0f, yaw, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
        else if (inputMag > 0.01f)
        {
            // 평상시엔 이동 방향으로 회전
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }

        // --- 중력/점프 ---
        if (_cc.isGrounded)
        {
            if (_verticalVelocity < 0f) _verticalVelocity = -2f;
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        _verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontal + Vector3.up * _verticalVelocity;
        _cc.Move(velocity * Time.deltaTime);

        // --- Animator 갱신(컨트롤러가 실제로 있을 때만) ---
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            // 조준 중엔 몸 로컬 기준 전/후/좌/우 이동을 표현하기 위해 방향 성분 전달도 가능하지만
            // 기본은 이동 속도 크기만 넘긴다.
            float animSpeed = (isAiming ? aimSpeed : (running ? runSpeed : walkSpeed)) * inputMag;
            animator.SetFloat(SpeedHash, animSpeed, 0.1f, Time.deltaTime);
            animator.SetBool(IsAimingHash, isAiming);
        }
    }

    private Vector2 ReadMoveInput()
    {
        var kb = Keyboard.current;
        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        return new Vector2(x, y);
    }
}
