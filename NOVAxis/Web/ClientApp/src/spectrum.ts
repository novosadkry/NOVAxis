import { useCallback, useEffect, useRef, useSyncExternalStore } from 'react'

/** How long a frame stands before the picture is treated as dead. */
const StaleMs = 700

export interface SpectrumFrame {
  bands: Uint8Array
  /** Client clock, so a renderer can fade out a picture nothing is feeding. */
  receivedAt: number
}

/**
 * The latest spectrum, per guild, held outside React entirely.
 *
 * Thirty frames a second through `useState` would re-render the page thirty times a
 * second, and with more than one canvas on screen it would cost more than everything the
 * server does for this feature put together. Frames land in a plain object; the canvases
 * read it from inside their own animation frame and nothing above them ever re-renders.
 */
const frames = new Map<string, SpectrumFrame>()

export function receiveSpectrum(guildId: string, encoded: string): void {
  const binary = atob(encoded)
  const bands = new Uint8Array(binary.length)

  for (let i = 0; i < binary.length; i++) bands[i] = binary.charCodeAt(i)

  frames.set(guildId, { bands, receivedAt: Date.now() })
}

export function forgetSpectrum(guildId: string): void {
  frames.delete(guildId)
}

/**
 * The newest frame for a guild, or null once nothing has arrived for a moment - which
 * covers a pause, a track ending, and the socket going away, without asking about any
 * of them.
 */
export function readSpectrum(guildId: string | null): SpectrumFrame | null {
  if (!guildId) return null

  const frame = frames.get(guildId)

  return frame && Date.now() - frame.receivedAt < StaleMs ? frame : null
}

/**
 * Asks the server for frames while the caller is on screen, and stops when it is not.
 * The server pushes nothing to a guild nobody has asked about, so a collapsed panel or a
 * background tab really does cost nothing rather than merely being ignored.
 */
export function useSpectrumFeed(
  guildId: string | null,
  request: (guildId: string, wanted: boolean) => void,
  enabled = true,
): void {
  const latest = useRef(request)
  latest.current = request

  useEffect(() => {
    if (!guildId || !enabled) return

    let asked = false

    const tell = (wanted: boolean) => {
      if (asked === wanted) return

      asked = wanted
      latest.current(guildId, wanted)
    }

    // A hidden tab still receives websocket messages even though it will not draw them,
    // so the asking has to stop rather than the drawing
    const follow = () => tell(!document.hidden)

    follow()
    document.addEventListener('visibilitychange', follow)

    return () => {
      document.removeEventListener('visibilitychange', follow)
      tell(false)
      forgetSpectrum(guildId)
    }
  }, [guildId, enabled])
}

/**
 * Feeds the store a made up signal at the rate the server would.
 *
 * The mock API used to drive this app in a browser has no SignalR, so without this there
 * is no way to look at the renderer at all short of running the bot and joining a voice
 * channel. Reachable only with ?spectrum=demo, and worth keeping afterwards: the floor,
 * the ceiling and the tilt are tuned by eye, and doing that against a real guild means a
 * restart per guess.
 */
export function startSpectrumDemo(guildId: string, bands = 48, fps = 30): () => void {
  let phase = 0

  const timer = window.setInterval(() => {
    phase += 1 / fps

    const frame = new Uint8Array(bands)
    const beat = Math.pow(Math.max(0, Math.sin(phase * Math.PI * 2 * 2)), 8)

    for (let i = 0; i < bands; i++) {
      const place = i / bands

      // A falling shape with a moving bump in it, plus a kick in the bottom bands - about
      // what music looks like once the tilt has been applied
      const shape = Math.pow(1 - place, 1.4) * 190
      const sweep = Math.exp(-Math.pow((place - (0.5 + 0.35 * Math.sin(phase * 0.7))) * 7, 2)) * 90
      const kick = place < 0.18 ? beat * 120 : 0

      frame[i] = Math.max(0, Math.min(255, shape + sweep + kick + Math.random() * 26 - 13))
    }

    frames.set(guildId, { bands: frame, receivedAt: Date.now() })
  }, 1000 / fps)

  return () => window.clearInterval(timer)
}

export type SpectrumMode = 'waterfall' | 'bars'
export type SpectrumVariant = 'panel' | 'ambient' | 'bar'

export interface SpectrumPrefs {
  mode: SpectrumMode
  variant: SpectrumVariant
}

const PrefsKey = 'novaxis.spectrum'
const Defaults: SpectrumPrefs = { mode: 'waterfall', variant: 'panel' }

function load(): SpectrumPrefs {
  try {
    const saved = window.localStorage.getItem(PrefsKey)
    return saved ? { ...Defaults, ...JSON.parse(saved) } : Defaults
  } catch {
    return Defaults
  }
}

let prefs = load()
const listeners = new Set<() => void>()

/**
 * Which rendering, and where it sits. Held outside React so the hero, the queue and the
 * transport bar all follow one answer, and kept per viewer so choosing is a matter of
 * looking rather than of rebuilding.
 */
export function useSpectrumPrefs() {
  const value = useSyncExternalStore(
    listener => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => prefs,
    () => Defaults,
  )

  const set = useCallback((next: Partial<SpectrumPrefs>) => {
    prefs = { ...prefs, ...next }

    try {
      window.localStorage.setItem(PrefsKey, JSON.stringify(prefs))
    } catch {
      // A viewer who blocks storage still gets to change it for this visit
    }

    listeners.forEach(listener => listener())
  }, [])

  return { ...value, set }
}
