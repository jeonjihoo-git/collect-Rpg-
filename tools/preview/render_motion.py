"""16무기 모션 미리보기 영상 렌더러(막대 인형).

입력: `dotnet run --project tools/Lumina.Sim -- motion out.json` 이 만든 관절 좌표(Core MotionLibrary 그대로).
출력: 4x4 격자 MP4(60fps, 게임과 같은 틱). 빨간 별 = 명중 프레임(피해·타격음이 나는 순간).
사용: python3 tools/preview/render_motion.py out.json motion.mp4          (matplotlib, ffmpeg 필요)
     python3 tools/preview/render_motion.py --contact out.json contact.png  (첫 명중 프레임 정지 화면)
실제 Unity 모형이 아니라 같은 포즈 계산을 단순 선으로 그린 것이다.
"""
import json
import math
import subprocess
import sys

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

CAT = {"Longsword": "melee", "Dagger": "melee", "Greatsword": "melee", "DualBlades": "melee", "Spear": "melee",
       "Gauntlet": "melee", "Scythe": "melee", "Axe": "melee", "Bow": "ranged", "Crossbow": "ranged", "Gun": "ranged",
       "Launcher": "ranged", "Staff": "magic", "Grimoire": "magic", "Scroll": "magic", "MagicRing": "magic"}
COL = {"melee": "#dfe4ee", "ranged": "#c9a55c", "magic": "#2bb5a6"}

# 게임 3/4 시점과 비슷한 카메라(뒤쪽 위·약간 오른쪽)
EYE = (2.6, 3.4, -5.0)
LOOK = (0.0, 1.0, 0.5)


def basis():
    f = [LOOK[i] - EYE[i] for i in range(3)]
    n = math.sqrt(sum(c * c for c in f))
    f = [c / n for c in f]
    up = (0.0, 1.0, 0.0)
    r = [up[1] * f[2] - up[2] * f[1], up[2] * f[0] - up[0] * f[2], up[0] * f[1] - up[1] * f[0]]
    n = math.sqrt(sum(c * c for c in r))
    r = [c / n for c in r]
    u = [f[1] * r[2] - f[2] * r[1], f[2] * r[0] - f[0] * r[2], f[0] * r[1] - f[1] * r[0]]
    return r, u, f


R, U, F = basis()


def proj(p):
    d = [p[i] - EYE[i] for i in range(3)]
    x = sum(d[i] * R[i] for i in range(3))
    y = sum(d[i] * U[i] for i in range(3))
    z = max(0.1, sum(d[i] * F[i] for i in range(3)))
    return (x / z * 6.0, y / z * 6.0)


def contact_sheet(data, out):
    """각 무기 일반 1타의 첫 명중 프레임 정지 화면 4x4(무기 끝이 몸 앞에서 접촉하는지 확인)."""
    fig, axes = plt.subplots(4, 4, figsize=(16, 9), dpi=100, facecolor="#121a2c")
    fig.subplots_adjust(0.01, 0.01, 0.99, 0.91, 0.05, 0.18)
    fig.suptitle("First hit frame of Normal 1 (contact pose)", color="#c9a55c", fontsize=13, y=0.985)
    for ax, w in zip(axes.flat, data["weapons"]):
        idx = next(i for i, f in enumerate(w["frames"]) if f["hit"])
        trail = [proj(w["frames"][j]["tip"]) for j in range(max(0, idx - 4), idx)] + [proj(w["contact"]["tip"])]
        draw(ax, w, w["contact"], trail, True)
    fig.savefig(out, facecolor=fig.get_facecolor())


def draw(ax, w, fr, trail, hit):
    ax.clear()
    ax.set_facecolor("#1b2540")
    ax.set_xlim(-2.6, 2.6)
    ax.set_ylim(-1.6, 1.9)
    ax.set_xticks([])
    ax.set_yticks([])
    for sp in ax.spines.values():
        sp.set_color("#26324f")
    ring = [proj((math.cos(a) * 1.2, 0.0, math.sin(a) * 1.2)) for a in [i * math.pi / 24 for i in range(49)]]
    ax.plot([p[0] for p in ring], [p[1] for p in ring], color="#2bb5a6", lw=0.8, alpha=0.4)
    fwd = [proj((0, 0, 0)), proj((0, 0, 1.4))]
    ax.plot([fwd[0][0], fwd[1][0]], [fwd[0][1], fwd[1][1]], color="#c9a55c", lw=0.8, alpha=0.5)

    def seg(a, b, c, lw):
        pa, pb = proj(fr[a]), proj(fr[b])
        ax.plot([pa[0], pb[0]], [pa[1], pb[1]], color=c, lw=lw, solid_capstyle="round")

    body = "#8a93a8"
    seg("hipR", "footR", body, 3)
    seg("hipL", "footL", body, 3)
    seg("hipR", "hipL", body, 3)
    seg("waist", "neck", body, 4)
    seg("shR", "shL", body, 3)
    seg("shR", "handR", "#b9c2d6", 3)
    seg("shL", "handL", "#b9c2d6", 3)
    ax.add_patch(plt.Circle(proj(fr["head"]), 0.13, color="#e8d5c4"))
    wc = COL[CAT[w["weapon"]]]
    seg("grip", "tip", wc, 4)
    if w["dual"]:
        seg("handL", "tipL", wc, 4)
    if len(trail) > 1 and fr["trail"]:
        ax.plot([p[0] for p in trail], [p[1] for p in trail], color=wc, lw=6, alpha=0.25)
    if hit:
        tp = proj(fr["tip"])
        ax.plot([tp[0]], [tp[1]], marker="*", ms=22, color="#ff4a3a")
    ax.set_title(f'{w["weapon"]}  {fr["label"]}', color="#e8e6df", fontsize=10)


def main():
    if sys.argv[1] == "--contact":
        contact_sheet(json.load(open(sys.argv[2])), sys.argv[3])
        return
    src, out = sys.argv[1], sys.argv[2]
    data = json.load(open(src))
    weapons = data["weapons"]
    fps = data["fps"]
    total = max(len(w["frames"]) for w in weapons)
    fig, axes = plt.subplots(4, 4, figsize=(16, 9), dpi=100, facecolor="#121a2c")
    fig.subplots_adjust(0.01, 0.01, 0.99, 0.91, 0.05, 0.18)
    fig.suptitle("LUMINA CHRONICLE - 16 weapon motion preview (stick figure, same pose math as Unity)  red star = hit frame",
                 color="#c9a55c", fontsize=13, y=0.985)
    w_px, h_px = fig.canvas.get_width_height()
    ff = subprocess.Popen(["ffmpeg", "-loglevel", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", f"{w_px}x{h_px}",
                           "-r", str(fps), "-i", "-", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "23", out],
                          stdin=subprocess.PIPE)
    trails = [[] for _ in weapons]
    hit_flash = [0 for _ in weapons]
    for fi in range(total):
        for k, (ax, w) in enumerate(zip(axes.flat, weapons)):
            frames = w["frames"]
            fr = frames[min(fi, len(frames) - 1)]
            trails[k].append(proj(fr["tip"]))
            trails[k] = trails[k][-7:]
            if fr["hit"]:
                hit_flash[k] = 3
            draw(ax, w, fr, trails[k], hit_flash[k] > 0)
            if hit_flash[k] > 0:
                hit_flash[k] -= 1
        fig.canvas.draw()
        ff.stdin.write(fig.canvas.buffer_rgba().tobytes())
    ff.stdin.close()
    ff.wait()
    print(f"frames {total} ({total / fps:.1f}s) -> {out}")


if __name__ == "__main__":
    main()
