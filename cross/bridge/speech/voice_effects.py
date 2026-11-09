"""Local, finite voice processing; no player, network or persistent delay state.

Coefficients define fixed pitch, formant and short-delay effects.
"""
from array import array
import hashlib
import math
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import tempfile

MODES = ('bright-dry-v1', 'bright-double-v1', 'bright-shifted-v1')
RATE = 24000
PITCH_SEMITONES = 2.0
TAIL_SAMPLES = 1920  # 80 ms; finite feed-forward paths cannot accumulate.


def digest(path):
    with Path(path).open('rb') as f:return hashlib.file_digest(f, 'sha256').hexdigest()


def validate_processor(value):
    if not isinstance(value, dict) or set(value) != {'path', 'sha256', 'rubberband_path', 'rubberband_sha256'}:
        raise ValueError('effects-processor-settings')
    for name in ('path', 'rubberband_path'):
        p = Path(value[name])
        if (not p.is_absolute() or p.is_symlink() or not p.is_file()
                or p.stat().st_mode & 0o022 or not re.fullmatch(r'[a-f0-9]{64}', value[name.replace('path', 'sha256')])
                or digest(p) != value[name.replace('path', 'sha256')]):
            raise ValueError('effects-processor-pin')
    if Path(value['path']).name != 'ffmpeg' or not os.access(value['path'], os.X_OK):
        raise ValueError('effects-processor-executable')


def digital_double(samples, *, grain=False):
    """Direct voice dominates; sparse grains are allowed only on interjections."""
    source = array('f', samples)
    if not source or len(source) > RATE * 44.9 or not all(math.isfinite(v) for v in source):
        raise ValueError('effects-samples')
    output = array('f', [0.0]) * (len(source) + TAIL_SAMPLES)
    pole, low = math.exp(-2 * math.pi * 850 / RATE), 0.0
    for n, sample in enumerate(source):
        low = (1-pole) * sample + pole * low
        high = sample - low
        output[n] += .90 * sample
        output[n+288] += .07 * high
        output[n+912] += .04 * high
    if grain and len(source) > 2400:
        # A single 35 ms, low-gain fragment near the opening interjection;
        # never place fixed percentage grains over names/numbers/technical text.
        width, delay, start = 840, 576, 960
        for k in range(width):
            window = .5 - .5 * math.cos(2*math.pi*k/(width-1))
            output[start+k] += .025 * window * source[start+k-delay]
    peak = max(abs(v) for v in output)
    gain = min(1.0, .98/peak) if peak else 1.0
    if gain < 1:output = array('f', (v*gain for v in output))
    return output, gain


def process(samples, profile, text, delivery=None):
    started = time.monotonic_ns()
    source = array('f', samples)
    if not source or len(source) > RATE*44.9 or not all(math.isfinite(v) for v in source):
        raise ValueError('effects-samples')
    mode = profile['effects']
    if mode not in MODES:raise ValueError('effects-mode')
    # Validate before every processor launch as well as warmup; no silent dry
    # fallback after an executable/library replacement or a failed filter.
    validate_processor(profile['processor'])
    formant = 'shifted' if mode == 'bright-shifted-v1' else 'preserved'
    effect = f'rubberband=tempo=1:pitch={2**(PITCH_SEMITONES/12):.12f}:formant={formant}:transients=smooth:detector=soft'
    if sys.byteorder != 'little':source.byteswap()
    style = delivery.style if delivery is not None else 'neutral'
    intensity = delivery.intensity if delivery is not None else 0
    if style not in ('neutral','playful','curious') or type(intensity) is not int or not 0 <= intensity <= 100:
        raise ValueError('effects-delivery')
    command_count = 0
    helper = Path(__file__).with_name('parent_exec.py')
    # A private inherited descriptor avoids shell/filter escaping of user paths.
    # Neutral retains the exact V8.1.1 static processor as an A/B control.
    with tempfile.TemporaryFile() as commands:
        descriptors = ()
        if intensity and style != 'neutral':
            knots = ([(0,0),(.18,.8),(.45,.2),(.72,-.4),(1,-1)] if style=='playful'
                     else [(0,0),(.45,-.3),(.72,.1),(.90,.8),(1,1.1)])
            duration = len(source)/RATE
            rows = []
            for index in range(math.ceil(duration/.04)):
                t = index*.04; fraction = min(1,t/duration)
                left,right = next((a,b)for a,b in zip(knots,knots[1:])if a[0]<=fraction<=b[0])
                relative = (left[1]+(right[1]-left[1])*(fraction-left[0])/(right[0]-left[0]))*intensity/100
                rows.append(f'{t:.5f} rubberband@tone pitch {2**((PITCH_SEMITONES+relative)/12):.12f};\n')
            commands.write(''.join(rows).encode('ascii'));commands.flush();commands.seek(0)
            command_count = len(rows);descriptors=(commands.fileno(),)
            effect = (f'asendcmd=f=/proc/self/fd/{commands.fileno()},'
                      f'rubberband@tone=tempo=1:pitch={2**(PITCH_SEMITONES/12):.12f}:'
                      f'formant={formant}:transients=smooth:detector=soft:window=short:pitchq=consistency')
        command = [sys.executable, '-I', str(helper), str(os.getpid()), '--', profile['processor']['path'],
                   '-hide_banner', '-loglevel', 'error', '-nostdin', '-threads', '1', '-filter_threads', '1',
                   '-f', 'f32le', '-ar', str(RATE), '-ac', '1', '-i', 'pipe:0', '-af', effect,
                   '-f', 'f32le', '-ar', str(RATE), '-ac', '1', 'pipe:1']
        result = subprocess.run(command, input=source.tobytes(), stdout=subprocess.PIPE,
                                stderr=subprocess.DEVNULL, pass_fds=descriptors, timeout=20, check=False)
    if result.returncode or not 0 < len(result.stdout) <= RATE*45*4 or len(result.stdout)%4:
        raise ValueError('effects-processor-failed')
    shifted = array('f');shifted.frombytes(result.stdout)
    if sys.byteorder != 'little':shifted.byteswap()
    if not all(math.isfinite(v) for v in shifted):raise ValueError('effects-nonfinite')
    if abs(len(shifted)-len(source)) > RATE//20:raise ValueError('effects-duration')
    # The selected filter keeps tempo fixed. Record its real speech duration;
    # final envelopes/approximate words use the processed clock, not old frames.
    speech_samples = len(shifted)
    words = re.findall(r"[a-z]+", text.lower())
    grain = (mode == 'bright-double-v1' and 0 < len(words) <= 5
             and words[0] in {'ha','hah','heh','ooh','whoa'}
             and set(words) <= {'ha','hah','heh','ooh','whoa','yes','got','it'}
             and not re.search(r'\d', text))
    gain = 1.0
    if mode == 'bright-double-v1':shifted, gain = digital_double(shifted, grain=grain)
    else:
        peak = max(abs(v) for v in shifted)
        gain = min(1.0,.98/peak) if peak else 1.0
        if gain < 1:shifted = array('f',(v*gain for v in shifted))
    return shifted, {'effects': mode, 'pitch_semitones': PITCH_SEMITONES, 'formants': formant,
        'delivery_style':style, 'delivery_intensity':intensity, 'pitch_commands':command_count,
        'tempo': 1.0, 'source_samples': len(source), 'speech_samples': speech_samples,
        'processed_samples': len(shifted), 'tail_samples': len(shifted)-speech_samples,
        'speech_duration_ms': round(speech_samples*1000/RATE), 'processor_gain': gain,
        'grain': grain, 'delay_state': 'clause-local; discarded before WAV publication',
        'effects_ms': (time.monotonic_ns()-started)/1e6}
