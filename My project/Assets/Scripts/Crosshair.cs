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
    [Tooltip("탄 퍼짐을 읽어올 대상. 비우면 씬에서 자동으로 찾는다.")]
    [SerializeField] private PlayerShooter shooter;

    [Header("표시 조건")]
    [Tooltip("조준(우클릭) 중에만 크로스헤어 표시. 발사도 조준 중에만 가능하므로 상태가 명확해진다.")]
    [SerializeField] private bool showOnlyWhenAiming = true;

    [Header("모양(px)")]
    [SerializeField] private float lineLength = 14f;  // 각 선 길이
    [SerializeField] private float gap = 7f;          // 중앙 간격
    [SerializeField] private float thickness = 3f;    // 선 두께
    [SerializeField] private bool showCenterDot = true;
    [Tooltip("선 뒤에 어두운 테두리를 그려 밝은 배경에서도 또렷하게 보이게 한다")]
    [SerializeField] private bool outline = true;
    [Tooltip("테두리 두께(px, 한쪽)")]
    [SerializeField] private float outlineWidth = 1.5f;
    [SerializeField] private Color outlineColor = new Color(0f, 0f, 0f, 0.75f);
    [Tooltip("조준 시 중앙 간격 배율(작을수록 좁아짐)")]
    [SerializeField] private float aimGapMultiplier = 0.5f;
    [Tooltip("탄 퍼짐에 따라 크로스헤어를 벌린다(퍼짐 원뿔을 화면 픽셀로 환산)")]
    [SerializeField] private bool spreadFollowsBullets = true;
    [Tooltip("벌어짐이 따라붙는 속도(급격한 튐 방지)")]
    [SerializeField] private float spreadSmooth = 14f;

    [Header("색")]
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color targetColor = new Color(1f, 0.25f, 0.25f, 0.95f);

    [Header("타겟 감지")]
    [SerializeField] private float range = 200f;
    [SerializeField] private LayerMask hitMask = ~0;

    private Texture2D _tex;
    private bool _onTarget;
    private float _spreadPixels; // 퍼짐을 환산한 화면 반경(px), 부드럽게 추적

    private void Awake()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        if (tpsCamera == null && aimCamera != null) tpsCamera = aimCamera.GetComponent<ThirdPersonCamera>();
        if (shooter == null) shooter = FindFirstObjectByType<PlayerShooter>();

        _tex = new Texture2D(1, 1);
        _tex.SetPixel(0, 0, Color.white);
        _tex.Apply();
    }

    private bool Visible => !showOnlyWhenAiming || (tpsCamera != null && tpsCamera.IsAiming);

    private void Update()
    {
        UpdateSpreadPixels();

        _onTarget = false;
        if (aimCamera == null || !Visible) return;

        // 화면 중앙 레이 = PlayerShooter의 조준 레이와 동일
        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
            _onTarget = hit.collider.GetComponentInParent<IDamageable>() != null;
    }

    /// <summary>
    /// 퍼짐 반각(도)을 화면 반경(px)으로 환산한다.
    /// 화면 절반 높이가 tan(FOV/2)에 대응하므로, tan(퍼짐)/tan(FOV/2) × (높이/2)가
    /// 실제 탄이 흩어지는 범위와 정확히 같은 픽셀 반경이 된다.
    /// </summary>
    private void UpdateSpreadPixels()
    {
        float targetPx = 0f;
        if (spreadFollowsBullets && shooter != null && aimCamera != null)
        {
            float halfFovTan = Mathf.Tan(aimCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            if (halfFovTan > 1e-5f)
            {
                float spreadTan = Mathf.Tan(shooter.CurrentSpreadDegrees * Mathf.Deg2Rad);
                targetPx = spreadTan / halfFovTan * (Screen.height * 0.5f);
            }
        }
        _spreadPixels = Mathf.Lerp(_spreadPixels, targetPx, 1f - Mathf.Exp(-spreadSmooth * Time.deltaTime));
    }

    private void OnGUI()
    {
        if (_tex == null || !Visible) return;

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        bool aiming = tpsCamera != null && tpsCamera.IsAiming;
        // 기본 간격 + 탄 퍼짐 반경 → 크로스헤어가 벌어진 만큼이 실제 탄착 범위
        float g = (aiming ? gap * aimGapMultiplier : gap) + _spreadPixels;
        float t = thickness;
        float half = t * 0.5f;

        Color prev = GUI.color;

        // 어두운 테두리를 먼저 깔면 밝은 배경(하늘·조명)에서도 흰 선이 묻히지 않는다
        if (outline)
        {
            GUI.color = outlineColor;
            DrawCross(cx, cy, g, t, half, outlineWidth);
        }

        GUI.color = _onTarget ? targetColor : normalColor;
        DrawCross(cx, cy, g, t, half, 0f);

        GUI.color = prev;
    }

    /// <summary>십자선 4개(+중앙 점)를 그린다. pad>0이면 각 변을 pad만큼 부풀려 테두리로 쓴다.</summary>
    private void DrawCross(float cx, float cy, float g, float t, float half, float pad)
    {
        DrawRect(cx - half - pad, cy - g - lineLength - pad, t + pad * 2f, lineLength + pad * 2f); // 위
        DrawRect(cx - half - pad, cy + g - pad, t + pad * 2f, lineLength + pad * 2f);             // 아래
        DrawRect(cx - g - lineLength - pad, cy - half - pad, lineLength + pad * 2f, t + pad * 2f); // 왼쪽
        DrawRect(cx + g - pad, cy - half - pad, lineLength + pad * 2f, t + pad * 2f);             // 오른쪽
        if (showCenterDot)
            DrawRect(cx - half - pad, cy - half - pad, t + pad * 2f, t + pad * 2f);               // 중앙 점
    }

    private void DrawRect(float x, float y, float w, float h)
    {
        GUI.DrawTexture(new Rect(x, y, w, h), _tex);
    }
}
