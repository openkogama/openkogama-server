import json
import os
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

SHOTS, OUT = sys.argv[1], sys.argv[2]
WIDTH, HEIGHT, FPS, SECONDS = 1080, 1920, 30, 20
FIRST, LAST = 30, 60
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"


def builds():
    rows = []
    for folder, _, files in os.walk(SHOTS):
        if "results.jsonl" not in files:
            continue
        for line in open(os.path.join(folder, "results.jsonl"), encoding="utf-8-sig"):
            row = json.loads(line)
            path = os.path.join(folder, row["file"]) if row.get("file") else None
            if path and os.path.exists(path) and row["ready"] and not row["blank"]:
                rows.append((row["timestamp"], row["version"], path))
    return sorted(rows)


def schedule(count):
    total = FPS * SECONDS
    if count == 1:
        return [total]
    middle = total - FIRST - LAST
    inner = count - 2
    frames = [FIRST]
    for i in range(inner):
        frames.append(middle * (i + 1) // max(inner, 1) - middle * i // max(inner, 1))
    frames.append(LAST if inner else total - FIRST)
    return frames


def label(image, year):
    draw = ImageDraw.Draw(image)
    font = ImageFont.truetype(FONT, 150)
    box = draw.textbbox((0, 0), year, font=font)
    x = (WIDTH - (box[2] - box[0])) // 2
    y = 260
    draw.text((x, y), year, font=font, fill="white", stroke_width=10, stroke_fill="black")
    return image


def main():
    rows = builds()
    print(len(rows), "builds")
    import datetime
    frames_dir = os.path.join(os.path.dirname(OUT) or ".", "frames")
    os.makedirs(frames_dir, exist_ok=True)
    index = 0
    for (timestamp, version, path), count in zip(rows, schedule(len(rows))):
        year = str(datetime.datetime.fromtimestamp(timestamp, datetime.timezone.utc).year)
        image = label(Image.open(path).convert("RGB").resize((WIDTH, HEIGHT)), year)
        for _ in range(count):
            image.save(os.path.join(frames_dir, f"{index:05d}.png"))
            index += 1
    subprocess.run(["ffmpeg", "-y", "-framerate", str(FPS), "-i", os.path.join(frames_dir, "%05d.png"),
                    "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart", OUT], check=True)
    print(index, "frames", OUT)


main()
