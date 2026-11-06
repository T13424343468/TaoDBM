#!/usr/bin/env python3
# TaoDBM 图标（2026-11-03 用户定稿：按 TaoZip 图标尺寸与内部元素比例重排）：
# 垂直渐变底上浅下深（天蓝系）；圆角 0.223；主体（太极）半径 0.43；
# 上下鱼眼为金色数据库圆柱替代圆点；无边框。
# 与 TaoZip 图标同构的比例系数：圆角/主体占位/眼睛细节均对齐其系数系。
from PIL import Image, ImageDraw
import os

SS = 4  # 超采样倍数
S = 256
W = S * SS

CORNER = 0.223       # 底板圆角（TaoZip 同款）
BODY_R = 0.43        # 主体半径（TaoZip 拉链环外沿同占位）
CYL_RX, CYL_RY, CYL_BODY = 0.17, 0.055, 0.30  # 鱼眼圆柱（宽度比同 TaoZip 拉链头 0.34）

SKY_TOP = (167, 213, 244)     # 渐变顶端
SKY_BOTTOM = (100, 168, 220)  # 渐变底端
YANG = (248, 250, 253)
YIN = (16, 42, 84)
GOLD_LIGHT = (255, 224, 130)
GOLD = (250, 189, 20)
GOLD_DARK = (212, 152, 8)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def make_base():
    """垂直渐变底（上浅下深，TaoZip 同构）。"""
    img = Image.new("RGB", (W, W), SKY_TOP)
    d = ImageDraw.Draw(img)
    for y in range(W):
        d.line([(0, y), (W, y)], fill=lerp(SKY_TOP, SKY_BOTTOM, y / W))
    return img


def draw_taiji(img):
    """居中太极：右半阴(玄蓝)、左半阳(青白)，无边框。"""
    d = ImageDraw.Draw(img)
    cx = cy = W // 2
    R = int(W * BODY_R)

    d.ellipse([cx - R, cy - R, cx + R, cy + R], fill=YANG)
    d.pieslice([cx - R, cy - R, cx + R, cy + R], start=-90, end=90, fill=YIN)
    r2 = R // 2
    d.ellipse([cx - r2, cy - R, cx + r2, cy], fill=YIN)
    d.ellipse([cx - r2, cy, cx + r2, cy + R], fill=YANG)
    return R


def draw_db_eyes(img, R):
    """上下两只鱼眼：金色数据库圆柱替代圆点（眼位 = 太极小圆圆心）。"""
    d = ImageDraw.Draw(img)
    cx = W // 2
    rx = int(R * CYL_RX)
    ry = int(R * CYL_RY)
    body = int(R * CYL_BODY)

    for eye_cy in (W // 2 - R // 2, W // 2 + R // 2):
        top = eye_cy - body // 2 - ry
        for i in range(body):
            col = lerp(GOLD_LIGHT, GOLD, i / body)
            d.line([(cx - rx, top + ry + i), (cx + rx, top + ry + i)], fill=col)
        d.ellipse([cx - rx, top + body, cx + rx, top + body + 2 * ry], fill=GOLD_DARK)
        d.ellipse([cx - rx, top, cx + rx, top + 2 * ry], fill=GOLD_LIGHT)
        d.ellipse([cx - rx, top, cx + rx, top + 2 * ry], outline=GOLD_DARK, width=SS)
        yk = top + ry + body // 2
        d.arc([cx - rx, yk - ry, cx + rx, yk + ry], start=0, end=180, fill=GOLD_DARK, width=SS)
        d.line([(cx - rx, top + ry), (cx - rx, top + ry + body)], fill=GOLD_DARK, width=SS)
        d.line([(cx + rx, top + ry), (cx + rx, top + ry + body)], fill=GOLD_DARK, width=SS)


def main():
    img = make_base()
    R = draw_taiji(img)
    draw_db_eyes(img, R)

    # 圆角裁切（TaoZip 同款 0.223）
    mask = Image.new("L", (W, W), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, W - 1, W - 1], radius=int(W * CORNER), fill=255)
    out = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    out.paste(img, (0, 0), mask)

    out_small = out.resize((S, S), Image.LANCZOS)

    base = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "TaoDBM", "Assets")
    base = os.path.normpath(base)
    os.makedirs(base, exist_ok=True)

    out_small.save(os.path.join(base, "TaoDBM.png"))
    out_small.save(os.path.join(base, "TaoDBM.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    prev = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icon_preview.png")
    out_small.save(prev)
    print("icon written to", base)


if __name__ == "__main__":
    main()
