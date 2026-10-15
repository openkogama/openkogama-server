import asyncio
import json
import os
import re
import subprocess
import sys
import time
import shutil
import urllib.request

from playwright.async_api import async_playwright

SITE = os.environ.get("SITE", "https://openkogama.org")
SERVER = "http://127.0.0.1:8080"
LIMIT = 600
SETTLE = 8000
REPORTS = ("not known", "no equivalent", "dropped", "rejected", "failed", "no server operation", "hid ", "unsupported", "exception", "error")


def find_word(path, word):
    try:
        import pytesseract
        from PIL import Image
        data = pytesseract.image_to_data(Image.open(path), output_type=pytesseract.Output.DICT)
    except Exception as error:
        print("ocr failed:", error, flush=True)
        return None
    for index, text in enumerate(data["text"]):
        if text.strip().upper() == word:
            return data["left"][index] + data["width"][index] / 2, data["top"][index] + data["height"][index] / 2
    print("ocr saw:", " ".join(text for text in data["text"] if text.strip())[:200], flush=True)
    return None


def find_button(path, box):
    from PIL import Image
    x0, y0, width, height = [int(value) for value in box]
    canvas = Image.open(path).convert("RGB").crop((x0, y0, x0 + width, y0 + height))
    small = canvas.resize((canvas.width // 2, canvas.height // 2))
    coarse = small.point(lambda value: value // 32 * 32 + 16)
    background = max(coarse.getcolors(small.width * small.height), key=lambda item: item[0])[1]
    pixels = small.load()
    left, right = int(small.width * 0.42), int(small.width * 0.58)
    rows = []
    for y in range(small.height):
        filled = sum(1 for x in range(left, right) if sum(abs(pixels[x, y][i] - background[i]) for i in range(3)) > 90)
        rows.append(filled >= (right - left) * 0.75)
    start = None
    for y, filled in enumerate(rows + [False]):
        if filled and start is None:
            start = y
        elif not filled and start is not None:
            if y - start >= 12:
                return x0 + width / 2, y0 + start + y
            start = None
    return None


def pick(versions, wanted):
    version, _, unity = wanted.partition("@")
    for entry in versions:
        if entry["version"] == version and (not unity or entry["unity"].startswith(unity)):
            return entry
    raise SystemExit(f"no webgl build for {wanted}")


class Server:
    def __init__(self, app, data):
        os.makedirs(os.path.join(data, "plugins"), exist_ok=True)
        json.dump({"publicUrl": SERVER, "mixedClients": True}, open(os.path.join(data, "server.json"), "w"))
        for name in os.listdir(os.path.join(app, "plugins")) if os.path.isdir(os.path.join(app, "plugins")) else []:
            if name.endswith(".dll"):
                shutil.copy(os.path.join(app, "plugins", name), os.path.join(data, "plugins", name))
        self.path = os.path.join(data, "server.log")
        self.process = subprocess.Popen(["dotnet", os.path.join(os.path.abspath(app), "openkogama-server.dll")], cwd=data,
                                        stdout=open(self.path, "w"), stderr=subprocess.STDOUT, env={**os.environ, "OPENKOGAMA_TRACE": "1"})

    def log(self):
        try:
            return open(self.path, errors="replace").read().splitlines()
        except FileNotFoundError:
            return []

    async def wait(self, text, start=0, limit=LIMIT):
        deadline = time.time() + limit
        while time.time() < deadline:
            lines = self.log()
            for index in range(start, len(lines)):
                if text in lines[index]:
                    return index + 1
            await asyncio.sleep(1)
        return None

    def create_world(self, template):
        request = urllib.request.Request(f"{SERVER}/api/worlds?name={template}&template={template}", method="POST", data=b"")
        return json.load(urllib.request.urlopen(request))["id"]

    def stop(self):
        self.process.terminate()
        try:
            self.process.wait(10)
        except subprocess.TimeoutExpired:
            self.process.kill()


class Client:
    def __init__(self, name, entry):
        self.name = name
        self.entry = entry
        self.logs = []
        self.page = None
        self.stage = "new"
        self.peer = None

    async def open(self, context, world, profile):
        self.page = await context.new_page()
        self.page.on("console", lambda message: self.logs.append(message.text[:300]))
        self.page.on("pageerror", lambda error: self.logs.append("pageerror " + str(error)[:300]))
        query = f"v={self.entry['id']}&mode=play&world={world}&profile={profile}&server={SERVER}"
        await self.page.goto(f"{SITE}/play/?{query}", wait_until="domcontentloaded")
        await self.page.wait_for_timeout(3000)
        await self.page.click("#play")
        self.stage = "clicked"

    async def error(self):
        return await self.page.evaluate("document.getElementById('error')?.textContent || ''")

    async def progress(self):
        return await self.page.evaluate("[document.getElementById('progress-text')?.textContent, document.getElementById('status')?.textContent, document.getElementById('error')?.textContent].filter(Boolean).join(' | ')")

    async def shot(self, out, label):
        await self.page.screenshot(path=os.path.join(out, f"{self.name}-{label}.png"))

    async def keys(self, *keys, hold=0):
        await self.page.evaluate("document.querySelector('canvas')?.focus()")
        for key in keys:
            if hold:
                await self.page.keyboard.down(key)
                await self.page.wait_for_timeout(hold)
                await self.page.keyboard.up(key)
            else:
                await self.page.keyboard.press(key)

    async def canvas(self):
        return await self.page.evaluate("(() => { const c = document.querySelector('canvas'); if (!c) return null; const r = c.getBoundingClientRect(); return [r.x, r.y, r.width, r.height]; })()")

    async def press_play(self, out):
        for attempt in range(10):
            path = os.path.join(out, f"{self.name}-menu.png")
            await self.page.screenshot(path=path)
            box = await self.canvas()
            point = find_word(path, "PLAY") or (box and find_button(path, box))
            if point:
                await self.page.mouse.move(*point)
                await self.page.wait_for_timeout(200)
                await self.page.mouse.click(*point, delay=120)
                self.logs.append(f"openkogama: test clicked play at {point}")
                return True
            await self.page.wait_for_timeout(5000)
        self.logs.append("openkogama: test found no play button")
        return False

    async def fuzz(self, step):
        await self.page.evaluate("document.querySelector('canvas')?.focus()")
        key = ["w", "a", "s", "d", "Space", "w"][step % 6]
        await self.page.keyboard.down(key)
        await self.page.wait_for_timeout(400)
        await self.page.keyboard.up(key)
        box = await self.canvas()
        if box and step % 3 == 0:
            await self.page.mouse.click(box[0] + box[2] * (0.3 + 0.1 * (step % 4)), box[1] + box[3] * 0.5, delay=60)

    async def say(self, text):
        await self.keys("Enter")
        await self.page.wait_for_timeout(500)
        await self.page.keyboard.type(text, delay=40)
        await self.page.keyboard.press("Enter")

    def result(self):
        lines = self.logs if self.stage not in ("playing", "world") else [line for line in self.logs if "rror" in line or "ailed" in line or "openkogama" in line]
        return {"name": self.name, "version": self.entry["version"], "unity": self.entry["unity"], "stage": self.stage, "peer": self.peer, "log": lines[-40:]}


async def enter(client, server, mark):
    start = time.time()
    joined = None
    while joined is None and time.time() - start < LIMIT:
        joined = await server.wait("joined", mark, 15)
        if joined is None:
            client.logs.append("openkogama: test page says " + await client.progress())
    if joined is None:
        client.stage = "no join"
        return mark
    client.peer = re.search(r"peer (\d+): joined", server.log()[joined - 1]).group(1)
    client.stage = "joined"
    synced = await server.wait(f"peer {client.peer}: world sent", joined, LIMIT - (time.time() - start))
    if synced is None:
        client.stage = "no sync"
        return joined
    client.stage = "world"
    return synced


async def main(app, wanted, out):
    os.makedirs(out, exist_ok=True)
    versions = json.load(urllib.request.urlopen(urllib.request.Request(SITE + "/versions.json", headers={"User-Agent": "openkogama-server"})))
    entries = [pick(versions, item) for item in wanted.split(",")]
    server = Server(app, os.path.join(out, "data"))
    result = {"versions": [entry["version"] + "@" + entry["unity"] for entry in entries], "clients": [], "checks": {}}
    clients = [Client(chr(ord("a") + index), entry) for index, entry in enumerate(entries)]
    try:
        if await server.wait("http 8080", 0, 120) is None:
            raise SystemExit("server did not start")
        world = server.create_world(os.environ["TEMPLATE"]) if os.environ.get("TEMPLATE") else 1
        result["world"] = world
        async with async_playwright() as p:
            browser = await p.chromium.launch(headless=True, args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist",
                "--disable-features=LocalNetworkAccessChecks,LocalNetworkAccessForWorkers,PrivateNetworkAccessSendPreflights,PrivateNetworkAccessRespectPreflightResults,BlockInsecurePrivateNetworkRequests"])
            mark = len(server.log())
            for index, client in enumerate(clients):
                context = await browser.new_context(viewport={"width": 1100, "height": 700})
                await client.open(context, world, index + 1)
                mark = await enter(client, server, mark)
                print(json.dumps({k: v for k, v in client.result().items() if k != "log"}), flush=True)
                if client.stage != "world":
                    await client.shot(out, "stuck")
            if all(client.stage == "world" for client in clients):
                await asyncio.sleep(6)
                for client in clients:
                    if await client.press_play(out):
                        client.stage = "playing"
                    else:
                        await client.shot(out, "stuck")
                await asyncio.sleep(8)
            inside = [client for client in clients if client.stage == "playing"]
            if len(inside) == len(clients):
                await asyncio.sleep(SETTLE / 1000)
                for client in clients:
                    await client.shot(out, "1-together")
                mover = clients[0]
                await mover.keys("w", hold=1500)
                await asyncio.sleep(3)
                for client in clients:
                    await client.shot(out, "2-moved")
                before = len(server.log())
                await mover.say("hello from " + mover.name)
                await asyncio.sleep(3)
                result["checks"]["chat"] = await server.wait("hello from " + mover.name, before, 10) is not None
                for client in clients:
                    await client.shot(out, "3-chat")
                last = clients[-1]
                await last.keys("Space")
                await asyncio.sleep(3)
                for client in clients:
                    await client.shot(out, "4-jump")
                before = len(server.log())
                for second in range(12):
                    for client in clients:
                        await client.fuzz(second)
                    await asyncio.sleep(1)
                await asyncio.sleep(3)
                for client in clients:
                    await client.shot(out, "5-fuzz")
                result["checks"]["fuzz clean"] = not any("exception" in line or "failed" in line for line in server.log()[before:])
                if os.environ.get("TEMPLATE") == "deadzone":
                    before = len(server.log())
                    await mover.say("/deadzone")
                    await asyncio.sleep(2)
                    await mover.say("/time night")
                    await asyncio.sleep(2)
                    await last.say("/pos")
                    await asyncio.sleep(30)
                    for client in clients:
                        await client.shot(out, "5b-zombies")
                    lines = server.log()[before:]
                    result["checks"]["zombies clean"] = not any("exception" in line or "failed" in line for line in lines)
                    result["checks"]["npcs"] = any("npc" in line.lower() for line in lines)
                before = len(server.log())
                gone = any(f"peer {last.peer}: gone" in line for line in server.log())
                await last.page.close()
                result["checks"]["leave"] = not gone and await server.wait(f"peer {last.peer}: gone", before, 90) is not None
                await asyncio.sleep(3)
                for client in clients[:-1]:
                    await client.shot(out, "6-left")
                mark = len(server.log())
                context = await browser.new_context(viewport={"width": 1100, "height": 700})
                await last.open(context, world, len(clients))
                await enter(last, server, mark)
                result["checks"]["rejoin"] = last.stage == "world"
                if last.stage == "world" and await last.press_play(out):
                    last.stage = "playing"
                    await asyncio.sleep(8)
                for client in clients:
                    await client.shot(out, "7-rejoined")
            await browser.close()
    finally:
        server.stop()
    lines = server.log()
    result["clients"] = [client.result() for client in clients]
    result["checks"]["one world"] = sum("play opened" in line for line in lines) == 1
    result["checks"]["all in"] = all(client.stage == "playing" for client in clients)
    result["reports"] = [line for line in lines if any(word in line.lower() for word in REPORTS)][:80]
    json.dump(result, open(os.path.join(out, "result.json"), "w"), indent=1)
    print(json.dumps(result["checks"]), flush=True)
    for line in result["reports"]:
        print(line)


asyncio.run(main(sys.argv[1], sys.argv[2], sys.argv[3]))
