# BossPatternFX — Unity URP 보스 패턴 이펙트 + 런너

바닥 경고 장판(원형·링·부채꼴·직선), 레이저 빔, 충격파, 탄막을
**셰이더 + 스크립트로 절차적으로** 만듭니다. 텍스처 에셋이나 프리팹을
미리 만들어 둘 필요가 없습니다.

- 요구사항: **Unity 2021.3 이상 + URP**
- 외부 의존성 없음. 텍스처도 런타임에 생성됩니다.
- 색상은 사이파이 챔버 레퍼런스에서 뽑은 보라/청색 계열이 기본값입니다.

---

## 1. 설치

`BossPatternFX` 폴더를 프로젝트의 `Assets/` 아래에 통째로 복사합니다.

```
Assets/BossPatternFX/
├── Runtime/
│   ├── Shaders/    BossFX.hlsl, BossTelegraph, BossBeam, BossRadial
│   ├── Scripts/    이펙트 컴포넌트 + 패턴 시스템
│   └── Textures/   선택 사항 (안 넣어도 동작)
└── Samples/        BossFXDemo.cs
```

`.shader` 와 `BossFX.hlsl` 은 **같은 폴더에 있어야** 합니다
(`#include "BossFX.hlsl"` 이 상대 경로입니다).

---

## 2. 3분 만에 확인하기

1. URP 씬을 하나 만듭니다
2. 빈 GameObject 에 **`BossFXDemo`** 스크립트를 붙입니다
3. Play

바닥, 더미 플레이어(WASD 로 이동), 보스, 패턴 5종이 코드로 생성됩니다.
장판에 맞으면 콘솔에 로그가 찍힙니다.

> **Bloom 을 켜세요.** 이 이펙트들은 전부 가산 합성 발광이라
> 포스트 프로세싱 Bloom 이 없으면 밋밋해 보입니다.
> URP: Volume 에 Bloom 추가 → Threshold 0.9 / Intensity 0.6 정도부터 시작.

---

## 3. 실전 사용법

### 3-1. 패턴 에셋 만들기

`Assets 우클릭 → Create → BossFX → Boss Pattern`

`steps` 리스트에 단계를 쌓습니다. 각 단계는

| 필드 | 의미 |
|---|---|
| `type` | Telegraph / Beam / Barrage / Impact / Wait |
| `origin` | Self, Target, TargetPredicted, ArenaCenter, RandomInArena |
| `faceTarget` | 플레이어 쪽을 바라보게 회전 |
| `repeat` / `repeatInterval` / `repeatAngleStep` | 연타·스윕 만들기 |
| `waitForCompletion` | 끄면 다음 단계와 겹쳐서 동시 진행 |
| `damage` | onHit 이벤트로 넘어가는 값 |

### 3-2. 러너 붙이기

보스 GameObject 에 **`BossPatternRunner`** 를 붙이고

- `patterns` — 만든 패턴 에셋들
- `target` — 플레이어 Transform
- `targetMask` — **플레이어 레이어만** 지정 (전부 켜두면 바닥까지 맞습니다)
- `arenaCenter` / `arenaRadius` — 무작위 배치 범위

### 3-3. 데미지 연결

`onHit` 이벤트에 함수를 연결하면 됩니다. 시그니처는 `BossHitInfo` 하나:

```csharp
public void OnBossHit(BossHitInfo info)
{
    // info.collider  맞은 대상
    // info.damage    스텝에 설정한 피해량
    // info.label     스텝 이름 (디버그용)
    // info.point     맞은 지점
    info.collider.GetComponent<Health>()?.TakeDamage(info.damage);
}
```

코드에서 직접 연결할 때는 `runner.onHit.AddListener(OnBossHit);`

### 3-4. 이펙트만 따로 쓰기

패턴 시스템 없이 한 방씩 쏘는 것도 됩니다.

```csharp
// 원형 경고 → 1.2초 뒤 발동
BossTelegraph.Spawn(
    new BossTelegraphSettings { shape = BossShape.Circle, radius = 5f, chargeTime = 1.2f },
    transform.position, Quaternion.identity,
    onFire: () => Debug.Log("쾅!"));

// 충격파
BossImpactFX.Spawn(new BossImpactSettings { radius = 8f, duration = 0.4f }, pos);

// 레이저
BossBeamFX.Spawn(new BossBeamSettings { length = 30f, width = 2f },
                 pos, rot, playerMask, col => Damage(col));

// 링 탄막 (코루틴)
StartCoroutine(BossBarrage.Emit(settings, transform, player, playerMask, col => Damage(col)));
```

---

## 4. 좌표 규칙 (중요)

경고로 보여준 모양과 실제 판정이 어긋나지 않도록 기준을 통일했습니다.

| 모양 | `position` 의 의미 | 크기 |
|---|---|---|
| Circle | 중심 | `radius` = 반지름 |
| Ring | 중심 | `radius` = 바깥 반지름, `innerRadius` = 안쪽 비율(0~1) |
| Cone | **꼭짓점** | `radius` = 사거리, `coneAngle` = 벌어진 각 |
| Line | **시작점** | `radius` = 길이, `lineWidth` = 두께 |

부채꼴과 직선은 **`transform.forward` 방향**으로 뻗습니다.

판정은 `BossHitTest.Query(settings, origin, rotation, mask)` 가
같은 규칙으로 수행하므로, 보이는 것과 맞는 것이 항상 일치합니다.

---

## 5. 패턴 레시피

데모(`BossFXDemo.BuildPatterns()`)에 5종이 코드로 들어 있습니다.
값만 인스펙터에 옮겨 담으면 됩니다.

| 패턴 | 구성 |
|---|---|
| **슬램** | Circle + `origin: TargetPredicted` + `repeat 3` + `randomOffset 1.5` |
| **도넛** | Ring `innerRadius 0.35` + `origin: ArenaCenter` — 가운데로 피하기 |
| **부채꼴 스윕** | Cone 70° + `repeat 4` + `repeatAngleStep 55` — 옆으로 훑기 |
| **레이저** | Line 텔레그래프(판정 없음) → Beam `sweepAngle 120` |
| **나선 탄막** | Barrage Spiral, `countPerWave 8` × `waves 14`, `spiralStep 13` |

응용 아이디어

- **안전지대 반전**: Ring 을 `innerRadius 0.7` 로 두고 바깥이 안전하게
- **십자 레이저**: Beam 4개를 `waitForCompletion: false` 로 동시에
- **점점 좁아지는 장판**: Circle `repeat` 하면서 `radius` 를 줄이는 스텝 여러 개
- **유도탄**: `BossBulletSettings.homing` 을 90 정도로

---

## 6. 성능

- 장판·충격파·탄환 모두 **드로우콜 1개짜리 쿼드 1장**입니다. 텍스처 페치도 1회.
- 탄환은 풀링됩니다 (`BossBullet.ClearPool()` 로 씬 전환 시 정리).
- 머티리얼은 셰이더당 1개를 공유하고 개별 값은 `MaterialPropertyBlock` 으로
  넘기므로 인스턴스가 늘어나지 않습니다.
- 모든 셰이더는 SRP Batcher 호환입니다 (프로퍼티가 전부 `UnityPerMaterial` 안에 있음).

---

## 7. 커스터마이징

### 색

`BossFXLibrary` 에 챔버 팔레트가 상수로 들어 있습니다
(`Purple`, `Violet`, `Blue`, `Cyan`, `Red`, `CoreHot`).
스텝별로 `baseColor` / `hotColor` / `edgeColor` 를 바꾸면 즉시 반영됩니다.

### 룬 문양 장판

`Textures/BossFX_GlyphRing.png` 을 텔레그래프 머티리얼의 `_NoiseTex` 에 넣고
`_NoiseStrength` 를 0.8 정도로 올리면 마법진 느낌이 납니다.
(Import Settings 에서 Wrap Mode 를 Clamp 로)

### 데칼로 쓰기

기본은 바닥에서 살짝 띄운 평면 쿼드입니다. 지형이 울퉁불퉁하면
URP Decal Renderer Feature 를 켜고 같은 셰이더를 데칼 셰이더로
포팅하는 편이 낫습니다.

---

## 8. 검증 상태 — 읽어주세요

이 패키지는 Unity 가 없는 환경에서 작성되었습니다. **컴파일 테스트를 하지
못했습니다.** 대신 아래를 검증했습니다.

**검증한 것**

- 셰이더의 SDF·필·합성 수식을 numpy 로 그대로 포팅해 실제 픽셀을 렌더링하고
  눈으로 확인했습니다 (`verify_bossfx.py`, 미리보기 이미지 3장)
- 도형 면적을 해석적으로 검산했습니다
  (원형 3.1419 vs π=3.1416, 부채꼴 90° 0.7823 vs 0.7854,
  링 inner=0.6 → 2.0108 vs 2.0106, 직선 w=0.15 → 0.6000 vs 0.6000)
- 부채꼴이 +forward 를 향하는지, 필이 단조 증가하는지 확인
- 전 파일 괄호 균형 / CBUFFER 누락 / HLSLPROGRAM 짝 검사

**이 과정에서 실제로 잡은 버그**

- 빔 셰이더의 `smoothstep(_Fire, _Fire - 0.06, along)` — `edge0 > edge1` 은
  HLSL 에서 동작이 정의되지 않습니다. 빔이 반대로 그려지거나 아예 사라졌습니다.
  `1.0 - smoothstep(_Fire - 0.06, _Fire, along)` 으로 수정했습니다.
- `GetComponent<T>() ?? AddComponent<T>()` — `??` 는 Unity 의 `==` 오버로드를
  우회해 파괴된 오브젝트를 통과시킵니다. 명시적 `== null` 체크로 변경.
- `UnityEvent<string>` 을 직접 필드로 두면 인스펙터에 안 나옵니다.
  구체 서브클래스(`BossStringEvent`)로 변경.

**확인하지 못한 것**

- 실제 셰이더 컴파일 (URP 버전별 include 경로, 문법)
- C# 컴파일
- 런타임 동작

처음 Play 했을 때 에러가 나면 그 메시지를 그대로 보내주세요. 대부분
URP 버전에 따른 include 경로나 API 이름 차이일 겁니다.

---

## 9. 챔버 맵 전용 패턴 6종

만드신 사이파이 지휘실 맵의 **실측 치수에 맞춰** 튜닝한 패턴입니다.
`BossChamberLayout` 에 맵 수치가 상수로 들어 있고, 모든 패턴이 그 값에서
파생되므로 맵 크기를 바꾸면 상수만 고치면 패턴이 따라옵니다.

### 적용 방법 (에디터)

메뉴 **BossFX ▸ 1. 챔버 보스 패턴 에셋 만들기** →
`Assets/BossPatternFX/Patterns/` 에 .asset 6개 생성
메뉴 **BossFX ▸ 2. 씬에 보스 배치하기** →
플랫폼 위(y=1.75)에 러너가 붙은 Boss 오브젝트 생성. `target` 과 `targetMask` 만 채우면 끝.

### 적용 방법 (코드 한 줄)

빈 GameObject 에 **`BossChamberDemo`** 를 붙이고 Play.
챔버 FBX 가 씬에 있으면 그대로 쓰고, 없으면 대용 바닥/플랫폼을 깔아줍니다.
`onlyPatternIndex` 로 특정 패턴 하나만 반복 재생할 수 있습니다.

### 패턴 목록

| # | 이름 | 맵을 어떻게 쓰는가 |
|---|---|---|
| 1 | **코어 과부하** | 도넛의 안전지대 반지름을 **플랫폼 반지름(11m)에 정확히 일치**시켰습니다. 플레이어를 중앙으로 몰아넣습니다 |
| 2 | **플랫폼 슬램** | 좁아진 플랫폼 위에서 이동 예측 지점에 원형 4연타 |
| 3 | **콘솔 과부하** | 맵에 실제로 서 있는 **콘솔 오벨리스크 8기 자리**(r=14.2, 22.5°+45°k)에서 순차 폭발 |
| 4 | **회전 부채꼴** | 사거리를 **팔각 모서리(28.1m)**로 잡아 구석에 숨어도 닿습니다. 100° x 6연타로 한 바퀴 |
| 5 | **팔각 격자 레이저** | **팔각 여덟 벽 정중앙 방향**으로 동시 발사. 중앙은 전부 위험, 바깥 링의 쐐기 틈이 안전 |
| 6 | **코어 탄막** | 발사 반지름을 플랫폼 가장자리(11.6m)에 맞춰 코어가 뿜는 것처럼. 마무리로 개방부(+Z) 방향 부채꼴 |

1번이 안으로 몰고 5번이 밖으로 밀어내므로, 순서대로 돌리면 자연스럽게
플레이어를 왔다 갔다 하게 만듭니다.

### 좌표계 주의

Blender → Unity FBX 기본 임포트 기준으로 맞췄습니다.

```
Blender (x, y, z)  →  Unity (x, z, y)
Blender +Y (개방부) →  Unity +Z
Blender yaw a      ↔  Unity yaw (90 - a)
```

임포트 축 설정을 바꾸셨다면 `BossChamberLayout.OpenBayYaw` 만 고치면 됩니다.

### 검증 결과

`verify_chamber_patterns.py` 가 C# 상수를 직접 읽어와 검산합니다.

- 개방부: Blender 90° → Unity yaw 0° = 패턴 설정값 **일치**
- 격자 레이저 8방향 `[0,45,…,315]` = 팔각 벽면 방향 **완전 일치**
- 콘솔 장판 8곳 `[22.5,67.5,…,337.5]` = 실제 콘솔 위치 **완전 일치**
- 도넛 안전지대 = 0.4231 x 26 = **11.00m** = 플랫폼 반지름
- 탄막 도달거리 = 11.6 + 8.5x3.2 = **38.8m** ≥ 벽 26m (중간에 사라지지 않음)
- 격자 레이저 길이 30.1m ≥ 모서리 28.1m

위험 영역을 위에서 내려다본 그림(`preview_chamber_patterns.png`)으로도 확인했습니다.
