using UnityEngine;

/// <summary>
/// 플레이어의 손 본(bone)에 무기를 붙여 들고 있게 만드는 컴포넌트.
/// Player 오브젝트(애니메이터가 있는 루트)에 추가해서 사용한다.
/// 손 본을 이름으로 자동 탐색하며, weaponPrefab을 지정하면 런타임에 생성해서 붙이고,
/// 이미 씬에 배치된 총이 있으면 existingWeapon에 넣어 그 오브젝트를 붙일 수도 있다.
/// </summary>
public class WeaponHolder : MonoBehaviour
{
    [Header("손 본 (Hand Bone)")]
    [Tooltip("무기를 붙일 손 본 이름. Mixamo 리그 기본값은 mixamorig:RightHand")]
    [SerializeField] private string handBoneName = "mixamorig:RightHand";

    [Tooltip("비워두면 handBoneName으로 자동 탐색한다. 직접 지정하면 이 Transform을 사용.")]
    [SerializeField] private Transform handBoneOverride;

    [Header("무기 (Weapon)")]
    [Tooltip("런타임에 생성할 총 프리팹/모델. existingWeapon이 지정되면 무시된다.")]
    [SerializeField] private GameObject weaponPrefab;

    [Tooltip("이미 씬에 있는 총 오브젝트를 손에 붙이고 싶을 때 지정.")]
    [SerializeField] private GameObject existingWeapon;

    [Tooltip("existingWeapon일 때, 에디터에서 손으로 맞춘 위치를 그대로 유지한다(체크 시 아래 오프셋 무시).")]
    [SerializeField] private bool keepExistingPlacement = true;

    [Header("손안 위치 보정 (Offset)")]
    [Tooltip("손 본 기준 로컬 위치. 손잡이가 손바닥에 오도록 조정한다.")]
    [SerializeField] private Vector3 localPosition = Vector3.zero;

    [Tooltip("손 본 기준 로컬 회전(오일러 각).")]
    [SerializeField] private Vector3 localEulerAngles = Vector3.zero;

    [Tooltip("무기 스케일 보정.")]
    [SerializeField] private Vector3 localScale = Vector3.one;

    private GameObject _spawnedWeapon;

    /// <summary>현재 들고 있는 무기(런타임 생성 또는 씬 배치). 조준 보정 등에서 참조.</summary>
    public GameObject CurrentWeapon => _spawnedWeapon != null ? _spawnedWeapon : existingWeapon;

    private void Start()
    {
        EquipWeapon();
    }

    /// <summary>손 본을 찾아 무기를 생성/부착한다.</summary>
    public void EquipWeapon()
    {
        Transform handBone = ResolveHandBone();
        if (handBone == null)
        {
            Debug.LogError($"[WeaponHolder] 손 본을 찾지 못했습니다: '{handBoneName}'. " +
                           "본 이름을 확인하거나 handBoneOverride에 직접 지정하세요.", this);
            return;
        }

        if (existingWeapon != null)
        {
            // 부모만 손 본으로 맞추고(이미 자식이면 로컬 유지), 배치는 에디터 값 그대로 둔다.
            if (existingWeapon.transform.parent != handBone)
                existingWeapon.transform.SetParent(handBone, false);

            if (!keepExistingPlacement)
                ApplyOffset(existingWeapon.transform);
            return;
        }

        if (weaponPrefab != null)
        {
            GameObject weapon = Instantiate(weaponPrefab, handBone);
            _spawnedWeapon = weapon;
            ApplyOffset(weapon.transform);
            return;
        }

        Debug.LogWarning("[WeaponHolder] weaponPrefab / existingWeapon 둘 다 비어 있습니다.", this);
    }

    private void ApplyOffset(Transform weapon)
    {
        weapon.localPosition = localPosition;
        weapon.localEulerAngles = localEulerAngles;
        weapon.localScale = localScale;
    }

    private Transform ResolveHandBone()
    {
        if (handBoneOverride != null)
            return handBoneOverride;
        return FindDeepChild(transform, handBoneName);
    }

    /// <summary>이름으로 하위 계층을 재귀 탐색한다.</summary>
    private static Transform FindDeepChild(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
                return child;

            Transform found = FindDeepChild(child, childName);
            if (found != null)
                return found;
        }
        return null;
    }

#if UNITY_EDITOR
    // 에디터에서 값을 바꿀 때 이미 붙어 있는 무기 위치를 즉시 갱신(플레이 중 조정 편의).
    private void OnValidate()
    {
        if (!Application.isPlaying) return;

        // 프리팹 생성 무기, 또는 수동 배치를 끈 existingWeapon만 오프셋으로 갱신.
        Transform w = null;
        if (_spawnedWeapon != null) w = _spawnedWeapon.transform;
        else if (existingWeapon != null && !keepExistingPlacement) w = existingWeapon.transform;
        if (w == null) return;

        w.localPosition = localPosition;
        w.localEulerAngles = localEulerAngles;
        w.localScale = localScale;
    }
#endif
}
