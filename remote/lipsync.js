(() => {
  'use strict';
  const clamp = value => (value < 0 ? 0 : value > 1 ? 1 : value);
  const make = () => {
    let loudest = 0.04, shown = { open: 0, wide: 0, round: 0 };
    const ease = (from, to, rise, fall) => from + (to - from) * (to > from ? rise : fall);
    return (samples, spectrum, rate, size) => {
      let sum = 0;
      for (let i = 0; i < samples.length; i++) sum += samples[i] * samples[i];
      const level = Math.sqrt(sum / Math.max(1, samples.length));
      loudest = Math.max(loudest * 0.997, level, 0.02);
      let low = 0, mid = 0, high = 0, rest = 0;
      if (spectrum && spectrum.length) {
        const step = rate / size;
        for (let i = 1; i < spectrum.length; i++) {
          const hz = i * step, power = Math.pow(10, spectrum[i] / 10);
          if (!(power > 0)) continue;
          if (hz < 120) continue;
          if (hz < 900) low += power;
          else if (hz < 1200) rest += power;
          else if (hz < 3000) mid += power;
          else if (hz > 4000 && hz < 9000) high += power;
        }
      }
      const total = low + mid + high + rest;
      let want = { open: 0, wide: 0, round: 0 };
      if (level > 0.006 && level > loudest * 0.07 && total > 0) {
        const loud = clamp((level / loudest - 0.07) / 0.75);
        const hiss = high / total, bright = mid / (low + mid + 1e-12), dark = low / total;
        want.open = Math.pow(loud, 0.75);
        want.wide = clamp((bright - 0.12) / 0.3) * 0.9;
        want.round = clamp((dark - 0.8) / 0.17) * (1 - want.wide) * 0.9;
        if (hiss > 0.35) {
          const teeth = clamp((hiss - 0.35) / 0.3);
          want.open *= 1 - 0.75 * teeth;
          want.wide = Math.max(want.wide, 0.55 * teeth);
          want.round *= 1 - teeth;
        }
      }
      shown = {
        open: ease(shown.open, want.open, 0.6, 0.32),
        wide: ease(shown.wide, want.wide, 0.4, 0.2),
        round: ease(shown.round, want.round, 0.4, 0.2)
      };
      return { open: shown.open, wide: shown.wide, round: shown.round, level };
    };
  };
  const api = { make };
  if (typeof module === 'object' && module.exports) module.exports = api;
  else window.CapriLipSync = api;
})();
