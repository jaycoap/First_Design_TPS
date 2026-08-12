# TPS 세팅 가이드

New Input System + URP 기준. 아래 순서대로 에디터에서 연결하면 이동/조준/발사가 동작한다.

## 스크립트 목록
- `WeaponHolder.cs` — 총을 손 본(mixamorig:RightHand)에 부착
- `ThirdPersonCamera.cs` — 오버숄더 TPS 카메라(조준 시 줌인)
- `PlayerController.cs` — 카메라 기준 이동/회전/중력/점프
- `PlayerShooter.cs` — 레이캐스트 발사, 연사, 이펙트, 데미지 전달
- `TargetDummy.cs` — 발사 테스트용 표적(IDamageable)
- `BossController.cs` — 보스(AlienMonster) AI: 추격/할퀴기/레이저/텔레포트
- `BossRig.cs` — 보스 팔·손 절차적 포즈(공격 클립 없이 팔을 직접 겨눔)
- `BossFx.cs` — 보스 이펙트(충전 구체/레이저 광선/텔레포트 섬광/발톱 궤적)
- `BossClone.cs` — 분신 처형 패턴의 분신(보스 모델 복제 껍데기, 피해를 받지 않는 표적)

## 1. 플레이어 준비
1. `Resources/Player/source/Idle.fbx`를 Hierarchy로 드래그.
2. FBX Import Settings → Rig 탭 → **Animation Type = Humanoid** → Apply.
3. Player에 컴포넌트 추가:
   - **CharacterController** (Center Y ≈ 0.9, Height ≈ 1.8, Radius ≈ 0.3)
   - **WeaponHolder**
   - **PlayerController**
   - **PlayerShooter**

## 2. 총 부착 (WeaponHolder)
1. `Resources/Gun/source/assault_rifle.fbx`를 씬에 드래그.
2. WeaponHolder의 `Existing Weapon`에 씬의 assault_rifle 지정
   (또는 프리팹으로 만들어 `Weapon Prefab`에 지정).
3. `Hand Bone Name`은 기본값 `mixamorig:RightHand` 그대로.
4. Play 후 `Local Position/Euler/Scale`로 손 안 위치 미세조정.
5. 총 끝에 빈 GameObject `Muzzle`를 자식으로 만들어 총구에 배치.

## 3. 카메라 (ThirdPersonCamera)
1. **Main Camera**에 `ThirdPersonCamera` 추가.
2. `Target`에 Player 지정. `Pivot Height`는 캐릭터 어깨/머리 높이(≈1.5).
3. `Collision Mask`에서 Player 레이어는 제외(자기 자신에 막히지 않도록).

## 4. 발사 연결 (PlayerShooter)
- `Aim Camera` = Main Camera (비워두면 Camera.main 자동)
- `Muzzle Point` = 2-5에서 만든 Muzzle
- `Hit Mask`에서 Player 레이어 제외(자기 몸에 맞지 않도록)
- 이펙트는 선택: Muzzle Flash(ParticleSystem), Impact Prefab, Tracer(LineRenderer)

## 5. 조작
- 이동: WASD / 달리기: Left Shift / 점프: Space / 구르기: 달리는 중 C
- 조준: 우클릭(길게) / 발사: 좌클릭 / 재장전: R
- 마우스: 카메라 회전(커서는 잠김)
- **협공(G)**: 크로스헤어가 겨누는 대상을 과거의 나와 함께 사격(타임포스 소모).
  적을 스스로 찾지 않으므로 **겨눈 대상만** 친다. 사격 중에도 끊김 없이 쓸 수 있다.
- 시간 능력(T): 슬로우 선택 모드 → 좌클릭 시간역행 / 우클릭 협공

## 6. 발사 테스트
- Cube 하나 만들고 `TargetDummy` 추가 → 쏘면 빨갛게 깜빡이고 체력 0에 파괴됨.

## 7. (선택) 애니메이션 연결
Animator Controller를 만들어 아래 파라미터를 쓰면 스크립트가 자동 갱신한다:
- `Speed` (Float) — 이동 블렌드용
- `IsAiming` (Bool) — 조준 상태
- `Fire` (Trigger) — 발사 순간

Mixamo 클립 매핑 예:
- Idle/걷기 → Blend Tree (Speed)
- 조준 대기 → `Rifle Aiming Idle`
- 발사 → `Firing Rifle` / `Aiming Firing Rifle`

각 애니메이션 FBX도 Rig = Humanoid로 임포트해야 아바타가 공유된다.
Animator가 없어도 이동/조준/발사는 정상 동작한다.

## 8. 보스 몬스터 (AlienMonster)

메뉴 **Tools/TPS/Setup Boss (AlienMonster)** 한 번이면 배치·연결이 끝난다.
(프리팹 배치 → 플레이어 키의 1.6배로 크기 보정 → CharacterController 실측 →
`BossController`/`BossRig`/`TimeRewindable` 부착 → `Boss.controller` 생성/연결)

### 행동 패턴
| 패턴 | 발동 조건 | 흐름 |
|---|---|---|
| 추격 | 항상 | **걷기만** 사용(달리기 모션 없음). 먼 거리는 텔레포트가 담당 |
| 근접 할퀴기 | `Melee Range` 안 | **연속 3타** — 오른팔 → 왼팔 → 오른팔(마무리)로 번갈아 후려침 |
| 레이저 | `Laser Min Range`~`Laser Range`, 시야 확보 | 왼팔을 뻗고 검지 끝에 빛이 맺힘 → 일렁임이 점점 빨라짐 → 발사 |
| 텔레포트 | 발동 거리 밖에 `Teleport Delay` 이상 머물면 — **쿨다운 없음** | 번쩍이며 사라짐 → 등 뒤(65%)/정면에 등장 → **충격파**(이동 30% 둔화 1.5초 + 운석) |
| **분신 처형** | 체력이 `Judgment Health Ratio`(30%) 이하로 떨어지는 순간 **1회** | 맵 밖 원주에 진짜 포함 10기 정렬 → 일제 충전 → 파훼 또는 일제 사격 |

### 회피
- 레이저는 발사 **`Laser Lock Time`(기본 0.4초) 전에 조준이 고정**된다.
  예고선이 진해지는 순간 구르기(달리는 중 `C`)로 옆으로 빠지면 광선을 피할 수 있다.
- 구르기 중에는 `PlayerStats`의 무적 프레임도 적용돼 근접 할퀴기도 흘릴 수 있다(타임포스 획득).

### 조정 포인트 (BossController 인스펙터)
- 거리/속도 수치는 모두 **사람 1.8m 기준**이며, 실제 캐릭터 키에 맞춰 런타임에 자동 환산된다.
- 난이도: `Melee Cooldown`, `Laser Cooldown`, `Laser Lock Time`(클수록 피하기 쉬움), `Max Health`
- 텔레포트 빈도: `Teleport Distance`(작을수록 자주 붙는다) / `Teleport Delay`
  - 실제 발동 거리 = `min(Teleport Distance, 아레나 반지름 × Teleport Arena Ratio)`.
    좁은 아레나에서 발동 거리가 맵보다 넓어 **텔레포트가 아예 안 나오는 것**을 막는다.
    (레이저를 쏠 거리대는 남기도록 `Laser Min Range`의 1.6배 아래로는 내려가지 않는다)
- 레이저 굵기는 `BossFx.Beam`의 `CoreWidth`/`GlowWidth` 상수로 조절(판정 반경은 `Laser Radius`)
- 연출: `Boss Color`(레이저·텔레포트 공통 색), `Melee Windup`(예비동작이 길수록 반응하기 쉬움)

### 참고
- 공격 전용 클립이 없어 팔 동작은 `BossRig`가 휴머노이드 본을 직접 돌려 만든다(리그 무관).
- `TimeRewindable`이 붙어 있어 시간역행 시 보스의 위치와 체력이 함께 과거로 돌아간다.
- 보스 체력은 화면 상단 중앙 바(HudUI)로 표시된다.

### 근접 연타 (할퀴기 콤보)

`Melee Combo Hits`(3회)만큼 **양팔을 번갈아** 휘두른다. 첫 타만 `Melee Windup`(0.5초)으로 크게 당기고,
이후는 `Melee Combo Interval`(0.16초)로 짧게 이어져 연타로 보인다.
마지막 타는 `Melee Finisher Scale`(1.6배)만큼 궤적이 크고 피해·사거리·전진이 늘어난다.

- 한 대의 피해 = `Melee Damage` × `Melee Combo Damage Scale`(0.55).
  3연타 전부 맞으면 원래 1타보다 아프지만, **각 타를 따로 회피**할 수 있다.
- 이전 타의 팔은 다음 예비동작 동안 가중치를 빼며 내려가므로 동작이 끊겨 보이지 않는다.
- 궤적(트레일)은 양손 손가락 끝 모두에 붙어 있다.

### 텔레포트 충격파 (둔화 + 운석)

보스가 등장하는 순간 두 가지가 함께 터진다.

- **이동 둔화**: `Teleport Slow Factor`(0.7 = 30% 감소)를 `Teleport Slow Time`(1.5초) 동안.
  구르기에는 적용되지 않으므로, 둔해진 상태에서도 회피 수단은 남아 있다.
- **운석**: 플레이어 주변에 예고 링이 뜨고 `Meteor Fall Time`(1.1초) 뒤 떨어진다.
  `Meteor Count`(4발) × `Meteor Damage`(16), 착탄 반경 `Meteor Radius` 안이면 피격.
  텔레포트에 쿨다운이 없으므로 운석 쪽에 `Meteor Cooldown`(4초)을 둬서 계속 쏟아지지 않게 했다.

> 둔화 + 운석 조합이 "텔레포트로 붙는다"를 실제 압박으로 만든다. 너무 아프면
> `Meteor Damage`를 낮추거나 `Meteor Fall Time`을 늘려(예고를 길게) 난이도를 조절한다.

### 분신 처형 (체력 30% 전용 패턴)

보스 체력이 30% 밑으로 떨어지는 순간 **한 번만** 발동한다.

1. 진짜 보스가 사라지고, **아레나 밖 원주에 진짜 포함 10기**가 같은 간격으로 늘어선다.
   (`Judgment Clone Count` + 1기 / 반지름 = 아레나 반지름 × `Judgment Ring Scale`)
2. 전원이 플레이어를 겨누고 레이저를 충전한다. **진짜만 충전 색이 다르다**
   (`Judgment Real Color` 주황 vs 분신은 `Boss Color` 보라).
3. `Judgment Charge Time`(6초) 안에 **진짜를 크로스헤어로 겨누고 `G`(협공)** 를 눌러야 파훼된다.
   - 일반 사격은 이 패턴 동안 진짜에게 **전혀 통하지 않는다** — 협공만이 해법.
   - 잘못 겨눠 분신에게 협공하면 타임포스만 소모된다(분신은 피해를 받지 않는 표적).
   - **패턴이 시작되기 전부터 날아오던 협공은 파훼로 인정되지 않는다.** 협공은 초당 여러 발이라
     그대로 두면 체력을 30%로 만든 그 협공의 다음 탄이 패턴을 즉시 씹어버린다.
     패턴 진입 시 진행 중인 협공은 중단되고(고스트 재충전 페널티는 없음), 새로 겨눠 발동해야 한다.
   - 파훼 성공 시 분신이 모두 소멸하고 보스가 `Judgment Stun Time` 동안 무방비로 굳는다(반격 기회).
4. 실패하면 10기가 `Judgment Volley Stagger`(0.12초) 간격으로 차례차례 발사한다.
   구르기 무적으로 전부 흘릴 수 없도록 벌려 두었으므로 사실상 사망한다.

> 분신은 보스 모델을 복제해 스크립트를 전부 떼어낸 껍데기(`BossClone`)다.
> 비활성 컨테이너 안에서 복제하므로 원본 스크립트의 Awake가 돌지 않는다.
> 화면 상단의 경고와 남은 시간 바는 `HudUI`가 그린다.

## 9. 원형 아레나 낙하 방지

메뉴 **Tools/TPS/Build Arena Wall (낙하 방지)** — 발판을 실측해 보이지 않는 벽을 세운다.

- 중심에서 32방향으로 바닥을 아래로 훑어 **발판이 끊기는 반지름**을 찾는다.
  (스테이지 모델의 바운즈는 배경 구조물까지 포함할 수 있어 쓰지 않는다)
- 그 둘레에 `ArenaWall`이 박스 콜라이더 36조각을 원형으로 배치한다. 렌더러가 없어 보이지 않고,
  CharacterController가 막히므로 **플레이어와 보스 모두** 밖으로 나가지 못한다.
- 벽은 전용 레이어(`ArenaWall`)에 올라가며, 카메라 충돌 / 사격 / 조준 / 보스 레이저 마스크에서 자동 제외된다.
  (보이지 않는 벽에 총알이 맞거나 카메라가 튕기는 것 방지)

크기가 안 맞으면 씬의 `ArenaWall` 인스펙터에서 `Radius`/`Height`를 고치고
**우클릭 → 벽 다시 만들기**. 선택하면 초록색 원으로 범위가 표시된다.

> 아레나 오브젝트에 Collider가 없으면 측정에 실패한다(FBX 임포터의 Generate Colliders 확인).
