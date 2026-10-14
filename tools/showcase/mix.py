# Builds the trailer's soundtrack and muxes it onto the recorded video.
#   python tools/showcase/mix.py <trailer_raw.mp4> <final.mp4>
# The showcase recorder logs every sound effect the game played and every narration cue with its
# frame number (<video>.events.tsv). This lays the battle theme under the whole video (ducked
# while the narrator speaks), drops each sound effect in at its exact frame and pitch, and places
# the narrator's lines, then muxes the mix onto the video with ffmpeg.
import os
import subprocess
import sys
import wave

import numpy as np

FF = r'C:\Users\ASUS\AppData\Local\Microsoft\WinGet\Links\ffmpeg.exe'
FP = r'C:\Users\ASUS\AppData\Local\Microsoft\WinGet\Links\ffprobe.exe'
HERE = os.path.dirname(os.path.abspath(__file__))
AUDIO = os.path.join(HERE, '..', '..', 'UnityProject', 'Assets', 'Audio')
SR = 44100
FPS = 30.0


def decode(path):
    """any audio file -> float32 stereo at SR"""
    raw = subprocess.run([FF, '-v', 'error', '-i', path, '-f', 's16le', '-ac', '2', '-ar', str(SR), '-'],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.int16).astype(np.float32).reshape(-1, 2) / 32768.0


def repitch(x, pitch):
    if abs(pitch - 1) < 0.01:
        return x
    n = int(len(x) / pitch)
    src = np.arange(n) * pitch
    return np.stack([np.interp(src, np.arange(len(x)), x[:, c]) for c in range(2)], axis=1).astype(np.float32)


def add(buf, clip, at, gain):
    i0 = int(at * SR)
    if i0 >= len(buf):
        return
    n = min(len(clip), len(buf) - i0)
    buf[i0:i0 + n] += clip[:n] * gain


def main(video, final):
    dur = float(subprocess.run([FP, '-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', video],
                               capture_output=True, text=True, check=True).stdout.strip())
    total = int(dur * SR) + SR
    sfx = np.zeros((total, 2), np.float32)
    voice = np.zeros((total, 2), np.float32)
    duck = np.ones(total, np.float32)
    cache = {}
    events = [l.rstrip('\n').split('\t') for l in open(video + '.events.tsv', encoding='utf-8').readlines()[1:]]
    n_sfx = n_say = 0
    for e in events:
        if len(e) < 5:
            continue
        frame, kind, name, vol, pitch = int(e[0]), e[1], e[2], float(e[3]), float(e[4])
        t = frame / FPS
        if kind == 'SFX':
            key = (name, round(pitch, 2))
            if key not in cache:
                path = os.path.join(AUDIO, name + '.mp3')
                if not os.path.exists(path):
                    continue
                cache[key] = repitch(decode(path), pitch)
            add(sfx, cache[key], t, vol * 0.85)
            n_sfx += 1
        elif kind == 'SAY':
            path = os.path.join(HERE, 'voice', name + '.wav')
            if not os.path.exists(path):
                continue
            v = decode(path)
            add(voice, v, t, 1.5)
            # duck the music (and a little of the effects) under the narrator, with soft ramps
            a, b = int(t * SR), min(total, int((t + len(v) / SR) * SR))
            ramp = int(0.25 * SR)
            duck[max(0, a - ramp):a] = np.minimum(duck[max(0, a - ramp):a], np.linspace(1, 0.35, a - max(0, a - ramp)))
            duck[a:b] = np.minimum(duck[a:b], 0.35)
            e2 = min(total, b + ramp)
            duck[b:e2] = np.minimum(duck[b:e2], np.linspace(0.35, 1, e2 - b))
            n_say += 1
    fade = int(2.0 * SR)
    game_audio = video + '.audio.f32'
    if os.path.exists(game_audio):
        # the game's real output (music, effects, ambience), resampled to the mix rate
        rate, ch = [int(x) for x in open(video + '.audio.txt').read().split()]
        g = np.fromfile(game_audio, dtype=np.float32).reshape(-1, ch)
        if ch == 1:
            g = np.repeat(g, 2, axis=1)
        if rate != SR:
            src = np.arange(int(len(g) * SR / rate)) * (rate / SR)
            g = np.stack([np.interp(src, np.arange(len(g)), g[:, c]) for c in range(2)], axis=1).astype(np.float32)
        base = np.zeros((total, 2), np.float32)
        base[:min(total, len(g))] = g[:total]
        base[-fade * 2:] *= np.linspace(1, 0, fade * 2)[:, None]
        mix = base * (0.45 + 0.55 * duck[:, None]) * 1.2 + voice
        print('using the game audio track', game_audio)
    else:
        music = decode(os.path.join(AUDIO, 'BattleTheme.mp3'))
        reps = int(np.ceil(total / len(music)))
        music = np.tile(music, (reps, 1))[:total] * 0.36
        # fade the music in and out
        music[:fade] *= np.linspace(0, 1, fade)[:, None]
        music[-fade * 2:] *= np.linspace(1, 0, fade * 2)[:, None]
        mix = music * duck[:, None] + sfx * (0.6 + 0.4 * duck[:, None]) + voice
    mix = np.tanh(mix * 1.1) / np.tanh(1.1)   # soft limiter
    out = (np.clip(mix, -1, 1) * 32767).astype(np.int16)
    wav = video + '.mix.wav'
    with wave.open(wav, 'wb') as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(out.tobytes())
    subprocess.run([FF, '-y', '-v', 'error', '-i', video, '-i', wav, '-map', '0:v', '-map', '1:a', '-c:v', 'copy',
                    '-c:a', 'aac', '-b:a', '192k', '-shortest', '-movflags', '+faststart', final], check=True)
    print(f'mixed {n_sfx} sound effects and {n_say} narration lines over {dur:.1f}s -> {final}')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
