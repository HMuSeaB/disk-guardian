import math
from PIL import Image, ImageDraw, ImageFilter

def create_disk_guardian_icon(output_path="disk-guardian.ico"):
    size = 512  # 超高分率渲染后降采样保证极致抗锯齿
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    center_x = size / 2
    center_y = size / 2

    # 1. 绘制外部六边形/圆角科技护盾轮廓
    # 盾牌多边形关键控制点
    shield_pts = [
        (center_x, 32),            # 顶端中心尖角
        (size - 64, 80),          # 右上肩
        (size - 64, 280),         # 右侧腰
        (center_x, size - 40),     # 底部尖端
        (64, 280),                # 左侧腰
        (64, 80),                 # 左上肩
    ]

    # 绘制外发光底阴影
    glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    glow_draw = ImageDraw.Draw(glow)
    glow_draw.polygon(shield_pts, fill=(14, 165, 233, 90))
    glow = glow.filter(ImageFilter.GaussianBlur(16))
    img.paste(glow, (0, 0), glow)

    # 盾牌深色金属背景
    draw.polygon(shield_pts, fill=(15, 23, 42, 245), outline=(56, 189, 248, 255), width=10)

    # 盾牌内圈高光青绿描边
    inner_pts = [
        (center_x, 56),
        (size - 84, 98),
        (size - 84, 268),
        (center_x, size - 68),
        (84, 268),
        (84, 98),
    ]
    draw.polygon(inner_pts, fill=(17, 24, 39, 255), outline=(20, 184, 166, 180), width=6)

    # 2. 绘制代表“磁盘存储 / 空间治理”的圆盘盘片 (Hard Disk Platter)
    disk_cx = center_x
    disk_cy = center_y - 20
    disk_r = 130

    # 磁盘基底渐变
    draw.ellipse([disk_cx - disk_r, disk_cy - disk_r, disk_cx + disk_r, disk_cy + disk_r],
                 fill=(30, 41, 59, 255), outline=(51, 65, 85, 255), width=8)

    # 磁道同心圆 (Storage Tracks)
    for r in [110, 85, 60]:
        draw.ellipse([disk_cx - r, disk_cy - r, disk_cx + r, disk_cy + r],
                     outline=(14, 165, 233, 80), width=3)

    # 磁盘中心主轴 (Spindle Hub)
    hub_r = 32
    draw.ellipse([disk_cx - hub_r, disk_cy - hub_r, disk_cx + hub_r, disk_cy + hub_r],
                 fill=(15, 23, 42, 255), outline=(20, 184, 166, 255), width=6)

    # 3. 绘制闪耀守护微标：中心穿透的科技之刃与保护锁/对勾 (Guardian Shield Mark)
    # 绘制前景科技菱形 / 闪电守护线
    guard_points = [
        (center_x, disk_cy - 70),
        (center_x + 55, disk_cy + 10),
        (center_x, disk_cy + 90),
        (center_x - 55, disk_cy + 10)
    ]
    draw.polygon(guard_points, fill=(6, 182, 212, 140), outline=(52, 211, 153, 255), width=8)

    # 绘制核心闪耀安全对勾 (100% PASS 守护象征)
    check_pts = [
        (center_x - 36, disk_cy + 8),
        (center_x - 10, disk_cy + 34),
        (center_x + 38, disk_cy - 24)
    ]
    draw.line(check_pts, fill=(240, 253, 250, 255), width=16, joint="curve")

    # 4. 生成多分辨率 Windows .ico 资源
    sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]
    icon_images = []
    for s in sizes:
        resized = img.resize(s, Image.Resampling.LANCZOS)
        icon_images.append(resized)

    # 保存
    icon_images[0].save(
        output_path,
        format="ICO",
        sizes=sizes,
        append_images=icon_images[1:]
    )
    print(f"Icon successfully generated at: {output_path}")

if __name__ == "__main__":
    create_disk_guardian_icon("disk-guardian.ico")
