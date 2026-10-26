/** Short tones made with WebAudio (no audio files). Silent where WebAudio is missing or blocked. */

type AudioCtor = typeof AudioContext

let ctx: AudioContext | null = null

function audioContext(): AudioContext | null {
  if (ctx) return ctx
  const w = window as Window & { webkitAudioContext?: AudioCtor }
  const Ctor: AudioCtor | undefined = window.AudioContext ?? w.webkitAudioContext
  if (!Ctor) return null
  try {
    ctx = new Ctor()
  } catch {
    ctx = null
  }
  return ctx
}

function tone(frequency: number, durationMs: number, type: OscillatorType, repeat = 1) {
  const ac = audioContext()
  if (!ac) return
  try {
    if (ac.state === 'suspended') void ac.resume()
    const step = durationMs / 1000
    for (let i = 0; i < repeat; i++) {
      const start = ac.currentTime + i * step * 1.4
      const osc = ac.createOscillator()
      const gain = ac.createGain()
      osc.type = type
      osc.frequency.value = frequency
      gain.gain.setValueAtTime(0.0001, start)
      gain.gain.exponentialRampToValueAtTime(0.2, start + 0.01)
      gain.gain.exponentialRampToValueAtTime(0.0001, start + step)
      osc.connect(gain).connect(ac.destination)
      osc.start(start)
      osc.stop(start + step + 0.02)
    }
  } catch {
    // Audio is a nice-to-have; the result card is the real signal.
  }
}

/** One high beep: welcome. */
export function playBeep() {
  tone(880, 140, 'sine')
}

/** Two low buzzes: anything that needs attention. */
export function playBuzz() {
  tone(160, 180, 'square', 2)
}
