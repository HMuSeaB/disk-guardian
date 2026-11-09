import os
from PIL import Image, ImageDraw, ImageFilter

def build_production_assets(source_path="assets/icon-source.jpg", ico_output="disk-guardian.ico"):
    """
    根据现代开发者桌面生态 (Raycast / Zed / AnyCursor / wbmux) 极简矢量标准，
    从设计源图精密切割 Squircle，生成带抗锯齿透明通道的多尺寸应用图标。
    """
    if not os.path.exists(source_path):
        raise FileNotFoundError(f"Source image not found: {source_path}")

    src = Image.open(source_path).convert('RGBA')

    # 精准裁剪 768x768 居中 Squircle
    bbox = (128, 128, 896, 896)
    cropped = src.crop(bbox)

    # 超高采样至 1024x1024
    render_size = 1024
    high_res = cropped.resize((render_size, render_size), Image.Resampling.LANCZOS)

    # 生成连续曲率 Squircle 蒙版 (圆角率 22.5%, 半径 230px)
    mask = Image.new('L', (render_size, render_size), 0)
    mask_draw = ImageDraw.Draw(mask)
    mask_draw.rounded_rectangle([0, 0, render_size, render_size], radius=230, fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(1.0))

    final_icon = Image.new('RGBA', (render_size, render_size), (0, 0, 0, 0))
    final_icon.paste(high_res, (0, 0), mask)

    os.makedirs('assets', exist_ok=True)

    # 1. 导出 512x512 PNG
    icon_512 = final_icon.resize((512, 512), Image.Resampling.LANCZOS)
    icon_512.save('assets/icon.png', format='PNG')
    print("Exported assets/icon.png (512x512)")

    # 2. 导出 256x256 PNG
    icon_256 = final_icon.resize((256, 256), Image.Resampling.LANCZOS)
    icon_256.save('assets/icon-256.png', format='PNG')
    print("Exported assets/icon-256.png (256x256)")

    # 3. 导出包含 6 档全尺寸链的 Windows PE 原生 .ico
    sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]
    icon_images = []
    for s in sizes:
        resized = final_icon.resize(s, Image.Resampling.LANCZOS)
        icon_images.append(resized)

    icon_images[0].save(
        ico_output,
        format='ICO',
        sizes=sizes,
        append_images=icon_images[1:]
    )
    print(f"Exported multi-resolution Windows ICO ({len(sizes)} sizes): {ico_output}")

if __name__ == "__main__":
    build_production_assets()
