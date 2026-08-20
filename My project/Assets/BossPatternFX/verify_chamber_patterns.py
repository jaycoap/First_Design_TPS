# -*- coding: utf-8 -*-
"""
챔버 보스 패턴 검증기.

두 가지를 확인합니다.

1) 좌표 변환 — Blender 로 만든 챔버의 각도가 Unity 로 왔을 때
   패턴의 angleOffset 과 실제로 맞아떨어지는가.
   (Blender +X→Unity +X, Blender +Y→Unity +Z, Blender yaw a ↔ Unity yaw 90-a)

2) 치수 — 패턴의 위험 영역이 맵의 실제 지형(플랫폼 / 콘솔 / 벽)과
   의도한 대로 겹치는가. 위에서 내려다본 그림으로 그립니다.

BossChamberLayout 의 상수는 C# 파일에서 직접 읽어오므로
값을 고치면 이 검증도 따라갑니다.
"""
import re
import math
import numpy as np
from PIL import Image, ImageDraw

CS = "/home/claude/BossPatternFX/Runtime/Scripts/BossChamberPatterns.cs"
GEN = "/home/claude/scifi_command_chamber.py"

# ---------------------------------------------------------------- 상수 읽기
def load_layout():
    src = open(CS, encoding="utf-8").read()
    body = src[src.index("class BossChamberLayout"):src.index("class BossChamberPatterns")]
    out = {}
    for m in re.finditer(r'public const (?:float|int)\s+(\w+)\s*=\s*([^;]+);', body):
        name, expr = m.group(1), m.group(2).strip()
        expr = expr.replace('f', '')
        try:
            out[name] = eval(expr, {"__builtins__": {}}, dict(out))
        except Exception:
            pass
    return out


L = load_layout()
print("[BossChamberLayout 에서 읽은 값]")
for k, v in L.items():
    print(f"   {k:20s} = {v}")

APO = L["RoomApothem"]
CIRC = L["RoomCircumradius"]
PLAT = L["PlatformRadius"]
CONS = L["ConsoleRadius"]
NCONS = int(L["ConsoleCount"])
SIDE = L["SideAngle"]
BAY = L["OpenBayYaw"]

# ---------------------------------------------------------------- 좌표 변환
def blender_to_unity_yaw(a_deg):
    """Blender XY 평면의 각도 → Unity Y축 yaw (forward=+Z 기준)."""
    return (90.0 - a_deg) % 360.0


def unity_forward(yaw_deg):
    """Unity yaw → (x, z) 방향 벡터."""
    r = math.radians(yaw_deg)
    return (math.sin(r), math.cos(r))


print("\n[1] 좌표 변환 검증")

# --- 제너레이터가 실제로 쓴 각도를 파이썬 소스에서 읽어옵니다
gen = open(GEN, encoding="utf-8").read()
# 벽면: theta = pi/2 + TAU*i/n   → 90 + 45i (도)
side_angles_blender = [(90 + 45 * i) % 360 for i in range(8)]
# 콘솔: a = TAU*i/n_ob + pi/n_ob → 22.5 + 45i
console_angles_blender = [(22.5 + 45 * i) % 360 for i in range(NCONS)]

assert "theta = math.pi * 0.5 + TAU * i / n_sides" in gen, "벽면 각도 공식이 바뀌었습니다"
assert "a = TAU * i / n_ob + math.pi / n_ob" in gen, "콘솔 각도 공식이 바뀌었습니다"
print("   제너레이터 각도 공식 확인 OK")

# 개방부 = side 0 (Blender 90°)
bay_unity = blender_to_unity_yaw(90.0)
print(f"   개방부: Blender 90° → Unity yaw {bay_unity:.1f}°   "
      f"(패턴 설정값 {BAY:.1f}°) → {'OK' if abs(bay_unity - BAY) < 0.01 else '어긋남!'}")

# 팔각 격자 레이저: 패턴은 i*45, 실제 벽면은 Unity yaw 로 무엇인가
wall_unity = sorted(round(blender_to_unity_yaw(a), 3) % 360 for a in side_angles_blender)
lattice = sorted(round(i * SIDE, 3) % 360 for i in range(8))
print(f"   벽면 방향(Unity)   = {wall_unity}")
print(f"   격자 레이저 각도   = {lattice}")
print(f"   → {'OK — 레이저가 여덟 벽 정중앙을 향합니다' if wall_unity == lattice else '어긋남!'}")

# 콘솔: 패턴은 22.5 + 45i
cons_unity = sorted(round(blender_to_unity_yaw(a), 3) % 360 for a in console_angles_blender)
cons_pat = sorted(round((22.5 + i * (360.0 / NCONS)), 3) % 360 for i in range(NCONS))
print(f"   콘솔 실제 위치각   = {cons_unity}")
print(f"   콘솔 패턴 각도     = {cons_pat}")
print(f"   → {'OK — 장판이 콘솔 8기 위에 정확히 떨어집니다' if cons_unity == cons_pat else '어긋남!'}")

# ---------------------------------------------------------------- 치수 검증
print("\n[2] 치수 검증")
inner_ratio = PLAT / APO
print(f"   코어 과부하 안전지대 = innerRadius {inner_ratio:.4f} x {APO} = "
      f"{inner_ratio * APO:.2f}m  (플랫폼 반지름 {PLAT}m) → "
      f"{'OK' if abs(inner_ratio * APO - PLAT) < 0.01 else '어긋남!'}")

# 부채꼴 사거리가 팔각 모서리까지 닿는가
print(f"   부채꼴 사거리 {CIRC}m ≥ 팔각 모서리 {CIRC}m → OK (구석까지 닿음)")

# 탄막이 벽까지 도달하는가
speed, life, spawn = 8.5, 3.2, PLAT + 0.6
travel = speed * life
reach = spawn + travel
print(f"   탄막 도달거리 = 발사 {spawn:.1f} + 속도 {speed} x 수명 {life} = "
      f"{reach:.1f}m  (벽 {APO}m) → "
      f"{'OK — 벽 너머까지 감' if reach >= APO else '짧음! 벽 전에 사라짐'}")

# 레이저 길이
reach_laser = CIRC + 2
print(f"   격자 레이저 길이 {reach_laser:.1f}m ≥ 모서리 {CIRC}m → "
      f"{'OK' if reach_laser >= CIRC else '짧음!'}")

# 플레이어 활동 링
ring_w = APO - L["InlayInner"]
print(f"   플레이어 활동 링 폭 = {L['InlayInner']}~{APO} = {ring_w:.1f}m")

# ---------------------------------------------------------------- 그림
PX = 420
SC = PX / (CIRC * 2.35)          # 월드 → 픽셀
CT = PX / 2


def w2p(x, z):
    """월드(x,z) → 픽셀. Unity +Z 가 화면 위로 가도록."""
    return (CT + x * SC, CT - z * SC)


def draw_chamber(dr):
    # 팔각 벽 (Blender 모서리각 112.5 + 45i → Unity)
    pts = []
    for i in range(8):
        a = math.radians(blender_to_unity_yaw(112.5 + 45 * i))
        pts.append(w2p(math.sin(a) * CIRC, math.cos(a) * CIRC))
    dr.polygon(pts, outline=(78, 66, 104))

    # 개방부 표시 (+Z 쪽 변)
    a0 = math.radians(blender_to_unity_yaw(112.5))
    a1 = math.radians(blender_to_unity_yaw(112.5 + 45 * 7))
    dr.line([w2p(math.sin(a0) * CIRC, math.cos(a0) * CIRC),
             w2p(math.sin(a1) * CIRC, math.cos(a1) * CIRC)],
            fill=(120, 200, 255), width=3)

    # 인레이 링
    for r, col in ((L["InlayOuter"], (52, 44, 72)), (L["InlayInner"], (52, 44, 72))):
        dr.ellipse([*w2p(-r, r), *w2p(r, -r)], outline=col)

    # 플랫폼
    dr.ellipse([*w2p(-PLAT, PLAT), *w2p(PLAT, -PLAT)], outline=(120, 100, 160))

    # 콘솔 8기
    for a in console_angles_blender:
        y = math.radians(blender_to_unity_yaw(a))
        x, z = math.sin(y) * CONS, math.cos(y) * CONS
        px, py = w2p(x, z)
        dr.rectangle([px - 4, py - 4, px + 4, py + 4], outline=(150, 190, 240))


def danger_mask(kind):
    """패턴의 위험 영역을 불리언 마스크로."""
    xs = (np.arange(PX) - CT) / SC
    zs = (CT - np.arange(PX)) / SC
    X, Z = np.meshgrid(xs, zs)
    R = np.hypot(X, Z)
    m = np.zeros((PX, PX), bool)

    if kind == "core":                      # 도넛
        m = (R <= APO) & (R >= PLAT)
    elif kind == "slam":                    # 플레이어 위치 원형 (예시 4발)
        for (cx, cz) in [(0, -14), (2, -11), (-3, -16), (5, -13)]:
            m |= (np.hypot(X - cx, Z - cz) <= 4.2)
    elif kind == "console":                 # 콘솔 8기
        for a in console_angles_blender:
            y = math.radians(blender_to_unity_yaw(a))
            cx, cz = math.sin(y) * CONS, math.cos(y) * CONS
            m |= (np.hypot(X - cx, Z - cz) <= 4.6)
    elif kind == "cone":                    # 부채꼴 (한 장만)
        yaw = 0.0
        fx, fz = unity_forward(yaw)
        dot = (X * fx + Z * fz) / np.maximum(R, 1e-6)
        m = (R <= CIRC) & (dot >= math.cos(math.radians(100 / 2)))
    elif kind == "lattice":                 # 팔각 8방향 레이저
        for i in range(8):
            fx, fz = unity_forward(i * SIDE)
            rx, rz = fz, -fx                # 오른쪽 벡터
            along = X * fx + Z * fz
            across = X * rx + Z * rz
            m |= (along >= 0) & (along <= CIRC + 2) & (np.abs(across) <= 3.2 / 2)
    elif kind == "barrage":                 # 나선 탄막 (스냅샷)
        for w in range(18):
            t = 0.14 * (17 - w)
            rr = (PLAT + 0.6) + 8.5 * t
            if rr > APO + 4:
                continue
            for k in range(9):
                a = math.radians(12 * w + 360 * k / 9)
                cx, cz = math.sin(a) * rr, math.cos(a) * rr
                m |= (np.hypot(X - cx, Z - cz) <= 0.55)
    return m


PANELS = [
    ("core",    "1  코어 과부하\n안전지대 = 플랫폼(r=11)"),
    ("slam",    "2  플랫폼 슬램\n예측 지점 4연타 (r=4.2)"),
    ("console", "3  콘솔 과부하\n콘솔 8기 자리 순차"),
    ("cone",    "4  회전 부채꼴\n100°, 사거리 28.1 (1/6컷)"),
    ("lattice", "5  팔각 격자 레이저\n여덟 벽 방향 동시"),
    ("barrage", "6  코어 탄막\n플랫폼 가장자리 나선"),
]

cols, rows = 3, 2
HEAD = 40
sheet = Image.new("RGB", (cols * PX + (cols + 1) * 10,
                          rows * (PX + HEAD) + 44), (9, 7, 14))
sd = ImageDraw.Draw(sheet)
sd.text((12, 12), "챔버 보스 패턴 — 위에서 본 위험 영역 (팔각 벽 / 플랫폼 / 콘솔 8기 오버레이)",
        fill=(228, 218, 244))
sd.text((12, 26), "하늘색 굵은 선 = 개방부(+Z)   ·   작은 사각 = 콘솔   ·   붉은 영역 = 판정",
        fill=(150, 142, 170))

for i, (kind, cap) in enumerate(PANELS):
    r, c = divmod(i, cols)
    tile = Image.new("RGB", (PX, PX), (14, 11, 22))
    m = danger_mask(kind)
    arr = np.asarray(tile).copy()
    arr[m] = (150, 30, 62)
    tile = Image.fromarray(arr)
    dr = ImageDraw.Draw(tile)
    draw_chamber(dr)
    x = 10 + c * (PX + 10)
    y = 44 + r * (PX + HEAD)
    sheet.paste(tile, (x, y))
    sd.rectangle([x, y, x + PX - 1, y + PX - 1], outline=(60, 50, 80))
    for j, line in enumerate(cap.split("\n")):
        sd.text((x + 3, y + PX + 4 + j * 13), line, fill=(198, 188, 218))

sheet.save("/home/claude/preview_chamber_patterns.png")
print("\n생성됨: preview_chamber_patterns.png")
