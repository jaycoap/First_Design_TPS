using UnityEngine;

/// <summary>
/// 화면 중앙 크로스헤어(조준점) HUD.
/// PlayerShooter가 "화면 중앙에서 카메라 정면으로 Raycast"한 지점을 탄착점으로 쓰므로,
/// 화면 중앙 크로스헤어가 실제 조준/명중 지점과 일치한다.
/// - OnGUI로 그려서 별도 Canvas/스프라이트가 필요 없다.
/// - 조준(우클릭) 시 살짝 좁아지고, 적(IDamageable)을 겨누면 색이 바뀐다.
/// Main Camera에 붙이면 참조를 비워도 자동으로 동작한다.
/// </summary>
public class Crosshair : MonoBehaviour
{
    [Header("참조(비우면 자동)")]
    [SerializeField] private Camera aimCamera;
    [SerializeField] private ThirdPersonCamera tpsCamera;

    [Header("표시 조건")]
    [Tooltip("조준(우클릭) 중에만 크로스헤어 표시. 발사도 조준 중에만 가능하므로 상태가 명확해진다.")]
    [SerializeField] private bool showOnlyWhenAiming = true;

    [Header("모양(px)")]
    [SerializeField] private float lineLength = 10f;  // 각 선 길이
    [SerializeField] private float gap = 6f;          // 중앙 간격
    [SerializeField] private float thickness = 2f;    // 선 두께
    [SerializeField] private bool showCenterDot = true;
    [Tooltip("조준 시 중앙 간격 배율(작을수록 좁아짐)")]
    [SerializeField] private float aimGapMultiplier = 0.5f;

    [Header("색")]
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.85f);
    [SerializeField] private Color targetColor = new Color(1f, 0.25f, 0.25f, 0.95f);

    [Header("타겟 감지")]
    [SerializeField] private float range = 200f;
    [SerializeField] private LayerMask hitMask = ~0;

    private Texture2D _tex;
    private bool _onTarget;

    private void Awake()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        if (tpsCamera == null && aimCamera != null) tpsCamera = aimCamera.GetComponent<ThirdPersonCamera>();

        _tex = new Texture2D(1, 1);
        _tex.SetPixel(0, 0, Color.white);
        _tex.Apply();
    }

    private bool Visible => !showOnlyWhenAiming || (tpsCamera != null && tpsCamera.IsAiming);

    private void Update()
    {
        _onTarget = false;
        if (aimCamera == null || !Visible) return;

        // 화면 중앙 레이 = PlayerShooter의 조준 레이와 동일
        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
            _onTarget = hit.collider.GetComponentInParent<IDamageable>() != null;
    }

    private void OnGUI()
    {
        if (_tex == null || !Visible) return;

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        bool aiming = tpsCamera != null && tpsCamera.IsAiming;
        float g = aiming ? gap * aimGapMultiplier : gap;
        float t = thickness;
        float half = t * 0.5f;

        Color prev = GUI.color;
        GUI.color = _onTarget ? targetColor : normalColor;

        DrawRect(cx - half, cy - g - lineLength, t, lineLength); // 위
        DrawRect(cx - half, cy + g, t, lineLength);              // 아래
        DrawRect(cx - g - lineLength, cy - half, lineLength, t); // 왼쪽
        DrawRect(cx + g, cy - half, lineLength, t);              // 오른쪽
        if (showCenterDot)
            DrawRect(cx - half, cy - half, t, t);                // 중앙 점

        GUI.color = prev;
    }

    private void DrawRect(float x, float y, float w, float h)
    {
        GUI.DrawTexture(new Rect(x, y, w, h), _tex);
    }
}
