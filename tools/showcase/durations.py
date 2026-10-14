# Measures each narration WAV and writes durations.json, which the showcase director uses to pace
# the trailer around the narrator.
import json
import os
import wave

d = os.path.dirname(os.path.abspath(__file__))
lines = json.load(open(os.path.join(d, 'lines.json'), encoding='utf-8'))['lines']
out = []
for l in lines:
    w = wave.open(os.path.join(d, 'voice', l['id'] + '.wav'))
    dur = w.getnframes() / w.getframerate()
    w.close()
    out.append({'id': l['id'], 'text': l['text'], 'dur': round(dur, 2)})
json.dump({'lines': out}, open(os.path.join(d, 'durations.json'), 'w', encoding='utf-8'), indent=1)
print(' '.join(f"{o['id']}={o['dur']}" for o in out))
