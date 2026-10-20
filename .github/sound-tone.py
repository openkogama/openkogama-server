import json
import sys

import numpy as np

FREQ = 1234
RATE = 44100


def make(folder):
    import lameenc
    import soundfile

    t = np.arange(int(3 * RATE)) / RATE
    wave = (0.6 * np.sin(2 * np.pi * FREQ * t)).astype(np.float32)
    pcm = (wave * 32767).astype("<i2")
    soundfile.write(f"{folder}/tone-wav.wav", pcm, RATE, subtype="PCM_16")
    soundfile.write(f"{folder}/tone-ogg.ogg", wave, RATE, format="OGG", subtype="VORBIS")
    encoder = lameenc.Encoder()
    encoder.set_bit_rate(128)
    encoder.set_in_sample_rate(RATE)
    encoder.set_channels(1)
    encoder.set_quality(2)
    with open(f"{folder}/tone-mp3.mp3", "wb") as mp3:
        mp3.write(encoder.encode(pcm.tobytes()) + encoder.flush())


def listen(seconds, path=None):
    import soundcard

    speaker = soundcard.default_speaker()
    microphone = soundcard.get_microphone(speaker.name, include_loopback=True)
    data = microphone.record(samplerate=RATE, numframes=int(RATE * seconds))
    mono = data.mean(axis=1) if data.ndim > 1 else data
    spectrum = np.abs(np.fft.rfft(mono * np.hanning(len(mono))))
    freqs = np.fft.rfftfreq(len(mono), 1 / RATE)
    audible = spectrum[freqs > 50].sum()
    tone = spectrum[(freqs > FREQ - 20) & (freqs < FREQ + 20)].sum()
    if path:
        import soundfile
        soundfile.write(path, mono, RATE)
    print(json.dumps({
        "speaker": speaker.name,
        "rms": round(float(np.sqrt(np.mean(mono ** 2))), 5),
        "peak": round(float(freqs[np.argmax(spectrum[1:]) + 1]), 1),
        "tone": round(float(tone / audible), 3) if audible > 0 else 0.0,
    }))


if sys.argv[1] == "make":
    make(sys.argv[2])
else:
    listen(float(sys.argv[2]), sys.argv[3] if len(sys.argv) > 3 else None)
