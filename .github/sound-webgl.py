import asyncio
import importlib.util
import json
import os
import re
import subprocess
import sys
import urllib.request

from playwright.async_api import async_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("mixed", os.path.join(HERE, "mixed-clients.py"))
mixed = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mixed)

TONE_SECONDS = 3.0

# The game hands every clip to the browser, so decoding and starting a buffer of the tone's length means it plays.
HOOK = """
(() => {
  const log = (...parts) => console.log("openkogama-audio " + parts.join(" "));
  for (const type of [window.AudioContext, window.webkitAudioContext]) {
    if (!type || type.prototype.__hooked) continue;
    type.prototype.__hooked = true;
    const decode = type.prototype.decodeAudioData;
    type.prototype.decodeAudioData = function (buffer, done, failed) {
      const size = buffer.byteLength;
      const head = Array.from(new Uint8Array(buffer, 0, Math.min(4, size)), byte => byte.toString(16).padStart(2, "0")).join("");
      return decode.call(this, buffer,
        decoded => { log("decoded", size, head, decoded.duration.toFixed(2), decoded.numberOfChannels); if (done) done(decoded); },
        error => { log("decode-failed", size, head, String(error)); if (failed) failed(error); });
    };
  }
  const start = AudioBufferSourceNode.prototype.start;
  AudioBufferSourceNode.prototype.start = function (...args) {
    log("start", this.buffer ? this.buffer.duration.toFixed(2) : "none", this.buffer ? this.buffer.numberOfChannels : 0, this.loop);
    return start.apply(this, args);
  };
})();
"""


def tone(lines, kind):
    for line in lines:
        parts = line.split()
        at = 4 if kind == "decoded" else 2
        if len(parts) > at + 1 and parts[1] == kind and parts[at] != "none":
            if abs(float(parts[at]) - TONE_SECONDS) < 0.25 and int(parts[at + 1]) == 1:
                return True
    return False


async def main(app, wanted, out, formats):
    os.makedirs(out, exist_ok=True)
    versions = json.load(urllib.request.urlopen(urllib.request.Request(mixed.SITE + "/versions.json", headers={"User-Agent": "openkogama-server"})))
    entry = mixed.pick(versions, wanted)
    data = os.path.join(out, "data")
    sounds = os.path.join(data, "plugins", "data", "sounds")
    os.makedirs(sounds, exist_ok=True)
    subprocess.run([sys.executable, os.path.join(HERE, "sound-tone.py"), "make", sounds], check=True)
    server = mixed.Server(app, data)
    results = []
    try:
        if await server.wait("http 8080", 0, 120) is None:
            raise SystemExit("server did not start")
        async with async_playwright() as p:
            browser = await p.chromium.launch(headless=True, args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist",
                "--autoplay-policy=no-user-gesture-required",
                "--disable-features=LocalNetworkAccessChecks,LocalNetworkAccessForWorkers,PrivateNetworkAccessSendPreflights,PrivateNetworkAccessRespectPreflightResults,BlockInsecurePrivateNetworkRequests"])
            context = await browser.new_context(viewport={"width": 640, "height": 400})
            await context.add_init_script(HOOK)
            for kind in formats:
                world = server.create_world("soundcheck-" + kind)
                mark = len(server.log())
                client = mixed.Client("a", entry)
                await client.open(context, world, 1)
                await mixed.enter(client, server, mark)
                if client.stage == "world":
                    await asyncio.sleep(6)
                    if await client.press_play(out):
                        client.stage = "playing"
                await asyncio.sleep(20)
                await client.shot(out, kind)
                audio = [line for line in client.logs if line.startswith("openkogama-audio")]
                lines = server.log()[mark:]
                built = next((re.search(r"bundle for (\S+)", line).group(1) for line in lines if re.search(rf"sound \d+ tone-{kind}: .* bundle for", line)), None)
                result = {
                    "build": wanted, "version": entry["version"], "unity": entry["unity"], "format": kind, "stage": client.stage, "key": built,
                    "decoded": tone(audio, "decoded"), "started": tone(audio, "start"),
                    "audio": audio[-12:],
                    "errors": [line for line in client.logs if re.search(r"rror|ailed|AssetBundle", line) and "openkogama-audio" not in line][-10:],
                }
                result["ok"] = result["decoded"] and result["started"]
                print(json.dumps({key: value for key, value in result.items() if key not in ("audio", "errors")}), flush=True)
                results.append(result)
                await client.page.close()
            await browser.close()
    finally:
        server.stop()
        json.dump(results, open(os.path.join(out, "results.json"), "w"), indent=1)


asyncio.run(main(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4].split(",")))
