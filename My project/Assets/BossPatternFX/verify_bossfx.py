# -*- coding: utf-8 -*-
"""
BossFX 셰이더 수식 검증기.

HLSL 셰이더(BossFX.hlsl / BossTelegraph / BossRadial / BossBeam)의
SDF·필·합성 수식을 그대로 numpy 로 포팅해서 실제 픽셀을 렌더링합니다.

Unity 없이도 "모양이 의도대로 나오는가"를 눈으로 확인하기 위한 것입니다.
셰이더를 수정하면 이 파일의 대응 함수도 같이 고쳐서 다시 돌려보세요.
"""
import numpy as np
from PIL import Image, ImageDraw

SIZE = 220
TIME = 0.0


def grid(size=SIZE):
    """uv → p = (uv-0.5)*2, 범위 [-1,1]"""
    u = (np.arange(size) + 0.5) / size
    ux, uy = np.meshgrid(u, u)
    return (ux - 0.5) * 2.0, (uy - 0.5) * 2.0


# ============================================================ BossFX.hlsl 포팅
def sd_circle(px, py):
    return np.hypot(px, py) - 1.0


def sd_ring(px, py, inner):
    l = np.hypot(px, py)
    return np.maximum(l - 1.0, inner - l)


def sd_cone(px, py, dir_rad, half_rad):
    l = np.hypot(px, py)
    a = np.arctan2(py, px) - dir_rad
    a = np.arctan2(np.sin(a), np.cos(a))
    angular = (np.abs(a) - half_rad) * np.maximum(l, 1e-4)
    return np.maximum(l - 1.0, angular)


def sd_box(px, py, hx, hy):
    dx, dy = np.abs(px) - hx, np.abs(py) - hy
    return (np.minimum(np.maximum(dx, dy), 0.0)
            + np.hypot(np.maximum(dx, 0.0), np.maximum(dy, 0.0)))


def shape_sd(px, py, shape, inner, dir_rad, half_rad, line_w):
    if shape == 0:
        return sd_circle(px, py)
    if shape == 1:
        return sd_ring(px, py, inner)
    if shape == 2:
        return sd_cone(px, py, dir_rad, half_rad)
    return sd_box(px, py, 1.0, max(line_w, 0.001))


def fill_coord(px, py, mode, dir_rad):
    if mode == 0:
        return np.clip(np.hypot(px, py), 0, 1)
    if mode == 1:
        a = np.arctan2(py, px) - dir_rad
        a = np.arctan2(np.sin(a), np.cos(a))
        return np.clip(a / (2 * np.pi) + 0.5, 0, 1)
    return np.clip(px * 0.5 + 0.5, 0, 1)


def aa(d, w):
    return np.clip(0.5 - d / max(w, 1e-5), 0, 1)


def smoothstep(e0, e1, x):
    # e0/e1 이 배열일 수도 있으므로 np.maximum 사용 (HLSL smoothstep 과 동일 동작)
    denom = np.maximum(np.asarray(e1) - np.asarray(e0), 1e-6)
    t = np.clip((x - e0) / denom, 0, 1)
    return t * t * (3 - 2 * t)


# ============================================================ Telegraph
def render_telegraph(shape=0, fill=0.5, inner=0.6, cone_deg=90, cone_dir_deg=0,
                     line_w=0.15, fill_mode=0, size=SIZE,
                     base=(0.35, 0.12, 0.75), hot=(1.0, 0.18, 0.35),
                     edge_col=(0.85, 0.55, 1.0),
                     base_alpha=0.18, edge_w=0.035, edge_i=2.2,
                     stripe_scale=7.0, stripe_speed=0.6, stripe_strength=0.35,
                     intensity=1.0, opacity=1.0):
    px, py = grid(size)
    dir_rad = np.radians(cone_dir_deg)
    half = np.radians(cone_deg) * 0.5

    d = shape_sd(px, py, shape, inner, dir_rad, half, line_w)
    w = 2.0 / size * 1.5          # fwidth 근사 (픽셀 하나 크기)
    inside = aa(d, w)
    edge = aa(np.abs(d) - edge_w, w)

    t = fill_coord(px, py, fill_mode, dir_rad)
    filled = 1.0 - smoothstep(fill - 0.02, fill + 0.02, t)
    front = np.exp(-((t - fill) * 26.0) ** 2) * (1.0 if fill > 0.001 else 0.0)

    s = np.modf((px + py) * stripe_scale - TIME * stripe_speed)[0]
    s = np.where(s < 0, s + 1, s)
    stripe = smoothstep(0.42, 0.5, np.abs(s - 0.5)) * stripe_strength

    col = (np.array(base)[None, None, :] * (1 - filled)[..., None]
           + np.array(hot)[None, None, :] * filled[..., None])
    ec = np.array(edge_col)[None, None, :]
    k = np.clip(edge, 0, 1)[..., None]
    col = col * (1 - k) + ec * k
    k2 = np.clip(front, 0, 1)[..., None]
    col = col * (1 - k2) + ec * k2

    a = inside * (base_alpha + stripe + filled * 0.9)
    a = a + edge * edge_i + front * inside * 1.5
    a = a * intensity * opacity

    # Blend SrcAlpha One → 검은 배경 위 가산 합성
    return np.clip(col * a[..., None], 0, 1)


# ============================================================ Radial
def render_radial(mode=0, radius=0.6, thickness=0.12, falloff=2.0,
                  spikes=10, sharp=5, size=SIZE,
                  core=(1.0, 0.85, 1.0), edge=(0.6, 0.2, 1.0),
                  intensity=3.0, opacity=1.0):
    px, py = grid(size)
    r = np.hypot(px, py)
    ang = np.arctan2(py, px)
    clip = 1.0 - smoothstep(0.98, 1.0, r)

    if mode == 0:
        dr = np.abs(r - radius) / max(thickness, 1e-4)
        mask = np.clip(1 - dr, 0, 1) ** falloff
        core_mask = np.clip(1 - dr * 2.2, 0, 1) ** falloff
    elif mode == 1:
        mask = np.clip(1 - r, 0, 1) ** falloff
        core_mask = np.clip(1 - r / max(thickness, 1e-4), 0, 1) ** 1.5
    else:
        sp = np.abs(np.cos(ang * max(spikes, 1) * 0.5)) ** sharp
        mask = (np.clip(1 - r, 0, 1) ** falloff) * (0.25 + 0.75 * sp)
        core_mask = np.clip(1 - r * 3.0, 0, 1) ** 2.0

    k = np.clip(core_mask, 0, 1)[..., None]
    col = np.array(edge)[None, None, :] * (1 - k) + np.array(core)[None, None, :] * k
    a = (mask + core_mask * 1.2) * clip * intensity * opacity
    return np.clip(col * a[..., None], 0, 1)


# ============================================================ Beam
def render_beam(charge=1.0, fire=1.0, core_w=0.10, glow_w=0.55, falloff=2.5,
                head_taper=0.25, size=SIZE,
                core=(1.0, 0.9, 1.0), glow=(0.55, 0.2, 1.0), intensity=3.0):
    u = (np.arange(size) + 0.5) / size
    along, acr = np.meshgrid(u, u)
    across = (acr - 0.5) * 2.0

    reach = 1.0 - smoothstep(fire - 0.06, fire, along)   # edge0 < edge1 필수
    taper = 1.0 + (np.clip(1 - along, 0, 1) - 1.0) * head_taper
    wscale = (0.12 + (1.0 - 0.12) * charge) * taper

    core_m = 1.0 - smoothstep(0.0, np.maximum(core_w * wscale, 1e-4), np.abs(across))
    glow_m = np.clip(1 - np.abs(across) / np.maximum(glow_w * wscale, 1e-4), 0, 1) ** falloff

    k = np.clip(core_m, 0, 1)[..., None]
    col = np.array(glow)[None, None, :] * (1 - k) + np.array(core)[None, None, :] * k
    a = (core_m * 1.4 + glow_m) * reach * intensity
    return np.clip(col * a[..., None], 0, 1)


# ============================================================ 시트 만들기
def to_img(arr):
    return Image.fromarray((arr * 255).astype(np.uint8))


def label_sheet(tiles, cols, title, path, pad=8, header=26):
    n = len(tiles)
    rows = (n + cols - 1) // cols
    W = cols * (SIZE + pad) + pad
    H = rows * (SIZE + pad + header) + pad + 34
    sheet = Image.new("RGB", (W, H), (10, 8, 16))
    dr = ImageDraw.Draw(sheet)
    dr.text((pad, 10), title, fill=(230, 220, 245))
    for i, (img, cap) in enumerate(tiles):
        r, c = divmod(i, cols)
        x = pad + c * (SIZE + pad)
        y = 34 + pad + r * (SIZE + pad + header)
        sheet.paste(img, (x, y))
        dr.rectangle([x, y, x + SIZE - 1, y + SIZE - 1], outline=(60, 50, 80))
        dr.text((x + 2, y + SIZE + 4), cap, fill=(190, 180, 210))
    sheet.save(path)
    return path


def main():
    # ---- 1) 텔레그래프: 모양 4종 × 채움 진행도 4단계
    tiles = []
    names = ["Circle", "Ring(inner .6)", "Cone 90°", "Line w=.15"]
    for shape, nm in enumerate(names):
        for fill in (0.0, 0.35, 0.7, 1.0):
            fm = 2 if shape == 3 else 0
            img = to_img(render_telegraph(shape=shape, fill=fill, fill_mode=fm))
            tiles.append((img, f"{nm}  fill={fill:.2f}"))
    label_sheet(tiles, 4, "BossFX/Telegraph  —  shape x fill",
                "/home/claude/preview_telegraph.png")

    # ---- 2) 부채꼴 각도 / 방향, 링 안쪽 반지름, 각도 스윕
    tiles = []
    for ang in (30, 60, 120, 240):
        tiles.append((to_img(render_telegraph(shape=2, fill=1.0, cone_deg=ang)),
                      f"Cone {ang}°"))
    for inner in (0.15, 0.4, 0.65, 0.85):
        tiles.append((to_img(render_telegraph(shape=1, fill=1.0, inner=inner)),
                      f"Ring inner={inner}"))
    for f in (0.25, 0.5, 0.75, 1.0):
        tiles.append((to_img(render_telegraph(shape=0, fill=f, fill_mode=1)),
                      f"Angular fill={f}"))
    for lw in (0.06, 0.15, 0.3, 0.6):
        tiles.append((to_img(render_telegraph(shape=3, fill=1.0, line_w=lw,
                                              fill_mode=2)), f"Line w={lw}"))
    label_sheet(tiles, 4, "BossFX/Telegraph  —  파라미터 변화",
                "/home/claude/preview_telegraph_params.png")

    # ---- 3) Radial + Beam
    tiles = []
    for r in (0.2, 0.45, 0.7, 0.95):
        tiles.append((to_img(render_radial(mode=0, radius=r)), f"Ring r={r}"))
    for th in (0.2, 0.45, 0.7, 0.95):
        tiles.append((to_img(render_radial(mode=1, thickness=th)), f"Orb core={th}"))
    for sp in (6, 10, 16, 24):
        tiles.append((to_img(render_radial(mode=2, spikes=sp)), f"Burst spikes={sp}"))
    for ch, fi, cap in ((0.0, 1.0, "charge 0 (예열)"), (0.35, 1.0, "charge .35"),
                        (1.0, 0.5, "fire .5 (뻗는 중)"), (1.0, 1.0, "fire 1 (완전 발사)")):
        tiles.append((to_img(render_beam(charge=ch, fire=fi)), f"Beam {cap}"))
    label_sheet(tiles, 4, "BossFX/Radial (Ring/Orb/Burst) + BossFX/Beam",
                "/home/claude/preview_radial_beam.png")

    print("생성됨:")
    print("  preview_telegraph.png")
    print("  preview_telegraph_params.png")
    print("  preview_radial_beam.png")

    # ---- 수치 검증: 도형이 실제로 기대한 면적/위치를 차지하는가
    print("\n[수치 검증]")
    px, py = grid(400)
    r = np.hypot(px, py)

    inside_circle = (sd_circle(px, py) < 0)
    area = inside_circle.mean() * 4.0                      # [-1,1]^2 = 면적 4
    print(f"  원형 면적  = {area:.4f}  (기대 π={np.pi:.4f}, 오차 {abs(area-np.pi):.4f})")

    for deg in (30, 90, 180):
        m = (sd_cone(px, py, 0.0, np.radians(deg) * 0.5) < 0)
        a = m.mean() * 4.0
        exp = np.pi * deg / 360.0
        print(f"  부채꼴 {deg:3d}° 면적 = {a:.4f}  (기대 {exp:.4f}, 오차 {abs(a-exp):.4f})")

    for inner in (0.3, 0.6):
        m = (sd_ring(px, py, inner) < 0)
        a = m.mean() * 4.0
        exp = np.pi * (1 - inner ** 2)
        print(f"  링 inner={inner} 면적 = {a:.4f}  (기대 {exp:.4f}, 오차 {abs(a-exp):.4f})")

    for lw in (0.15, 0.4):
        m = (sd_box(px, py, 1.0, lw) < 0)
        a = m.mean() * 4.0
        exp = 2.0 * 2.0 * lw
        print(f"  직선 w={lw} 면적 = {a:.4f}  (기대 {exp:.4f}, 오차 {abs(a-exp):.4f})")

    # 부채꼴이 +X 를 향하는지 (셰이더 p.x = local Z = transform.forward)
    m = (sd_cone(px, py, 0.0, np.radians(60) * 0.5) < 0)
    cx = px[m].mean()
    cy = py[m].mean()
    print(f"  부채꼴 무게중심 = ({cx:+.3f}, {cy:+.3f})  → +x 방향이어야 함: "
          f"{'OK' if cx > 0.3 and abs(cy) < 0.02 else '문제!'}")

    # 필 진행도가 단조 증가하는지
    for mode, nm in ((0, "Radial"), (2, "Linear")):
        prev = -1
        ok = True
        for f in np.linspace(0, 1, 21):
            t = fill_coord(px, py, mode, 0.0)
            covered = float((t < f).mean())
            if covered < prev - 1e-6:
                ok = False
            prev = covered
        print(f"  {nm} 필 단조 증가: {'OK' if ok else '문제!'}")


if __name__ == "__main__":
    main()
