# TPS 세팅 가이드

New Input System + URP 기준. 아래 순서대로 에디터에서 연결하면 이동/조준/발사가 동작한다.

## 스크립트 목록
- `WeaponHolder.cs` — 총을 손 본(mixamorig:RightHand)에 부착
- `ThirdPersonCamera.cs` — 오버숄더 TPS 카메라(조준 시 줌인)
- `PlayerController.cs` — 카메라 기준 이동/회전/중력/점프
- `PlayerShooter.cs` — 레이캐스트 발사, 연사, 이펙트, 데미지 전달
- `TargetDummy.cs` — 발사 테스트용 표적(IDamageable)

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
- 이동: WASD / 달리기: Left Shift / 점프: Space
- 조준: 우클릭(길게) / 발사: 좌클릭
- 마우스: 카메라 회전(커서는 잠김)

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
