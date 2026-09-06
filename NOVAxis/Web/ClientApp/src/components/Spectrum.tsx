import { useEffect, useRef } from 'react'

import { useSpectrumRequest } from '../player'
import { readSpectrum, useSpectrumFeed } from '../spectrum'

export type SpectrumMode = 'waterfall' | 'bars'
export type SpectrumVariant = 'panel' | 'ambient' | 'bar'

interface SpectrumProps {
  guildId: string | null
  mode: SpectrumMode
  variant: SpectrumVariant
  /** False where the backend cannot produce one at all, so nothing is asked for. */
  available: boolean
}

/** How tall each placement wants to be, in CSS pixels. */
const Heights: Record<SpectrumVariant, number> = {
  panel: 140,
  ambient: 120,
  bar: 26,
}

/** How many columns of history the waterfall keeps. */
const History = 240

/**
 * Quiet to loud, through the two colours this interface already uses. The cold end is the
 * page's own background rather than a colour of its own: silence should read as nothing,
 * and a spectrogram whose quiet half glows is mostly glow.
 */
function palette(value: number): string {
  const level = value / 255

  if (level < 0.5) {
    const t = level / 0.5
    return `rgb(${Math.round(11 + t * 41)}, ${Math.round(14 + t * 217)}, ${Math.round(20 + t * 211)})`
  }

  const t = (level - 0.5) / 0.5
  return `rgb(${Math.round(52 + t * 188)}, ${Math.round(231 - t * 51)}, ${Math.round(231 - t * 190)})`
}

/**
 * What the bot is playing, drawn from bands the server computes and pushes.
 *
 * Both renderings read the same numbers; only the drawing differs. The waterfall shows
 * history, which as a side effect hides the fact that the picture leads the sound by
 * however long Discord takes to deliver it - bars, being an instant, do not.
 */
export function Spectrum({ guildId, mode, variant, available }: SpectrumProps) {
  const canvas = useRef<HTMLCanvasElement>(null)
  const { requestSpectrum, live } = useSpectrumRequest()

  useSpectrumFeed(guildId, requestSpectrum, available && live)

  useEffect(() => {
    const element = canvas.current

    if (!element || !available) return

    const context = element.getContext('2d')

    if (!context) return

    // The waterfall is drawn once per arriving frame into an offscreen strip and blitted
    // from there. Scrolling per animation frame instead would warp the time axis with
    // whatever jitter the network had that second
    const strip = document.createElement('canvas')
    const stripContext = strip.getContext('2d')

    let bars: Float32Array | null = null
    let peaks: Float32Array | null = null
    let columns = 0
    let seen = 0
    let frame = 0

    const resize = () => {
      const ratio = Math.min(window.devicePixelRatio || 1, 2)
      const width = element.clientWidth || 1
      const height = Heights[variant]

      element.width = Math.floor(width * ratio)
      element.height = Math.floor(height * ratio)
      element.style.height = `${height}px`

      context.setTransform(ratio, 0, 0, ratio, 0, 0)
    }

    resize()

    const observer = new ResizeObserver(resize)
    observer.observe(element)

    const draw = () => {
      frame = requestAnimationFrame(draw)

      const width = element.clientWidth || 1
      const height = Heights[variant]
      const latest = readSpectrum(guildId)

      context.clearRect(0, 0, width, height)

      if (!latest) {
        // Nothing arriving: let the picture go rather than freeze on the last thing heard
        if (bars) bars.fill(0)
        if (peaks) peaks.fill(0)
        seen = 0
        return
      }

      const { bands, receivedAt } = latest
      const count = bands.length

      if (!bars || bars.length !== count) {
        bars = new Float32Array(count)
        peaks = new Float32Array(count)
      }

      if (mode === 'bars') {
        const gap = width / count
        const thickness = Math.max(gap - 2, 1)

        for (let i = 0; i < count; i++) {
          const value = bands[i] / 255

          // Straight up, slow down: a bar which fell as fast as it rose would flicker
          bars[i] = Math.max(value, bars[i] * 0.86)
          peaks![i] = Math.max(bars[i], peaks![i] - 0.008)

          const tall = bars[i] * height

          context.fillStyle = palette(bands[i])
          context.fillRect(i * gap, height - tall, thickness, tall)

          const peak = peaks![i] * height

          if (peak > 2) {
            context.fillStyle = 'rgba(233, 238, 246, 0.55)'
            context.fillRect(i * gap, height - peak - 1, thickness, 1.5)
          }
        }

        return
      }

      if (!stripContext) return

      if (strip.width !== History || strip.height !== count) {
        strip.width = History
        strip.height = count

        // History nobody has yet is drawn as silence rather than left transparent: an
        // empty strip reads as a hole in the panel for the eight seconds it takes to fill
        stripContext.fillStyle = palette(0)
        stripContext.fillRect(0, 0, History, count)
      }

      // One column per frame that actually arrived, so the time axis stays honest
      if (receivedAt !== seen) {
        seen = receivedAt
        columns = Math.min(columns + 1, History)

        stripContext.drawImage(strip, -1, 0)

        for (let i = 0; i < count; i++) {
          stripContext.fillStyle = palette(bands[i])
          stripContext.fillRect(History - 1, count - 1 - i, 1, 1)
        }
      }

      context.imageSmoothingEnabled = true
      context.drawImage(strip, 0, 0, width, height)
    }

    frame = requestAnimationFrame(draw)

    return () => {
      cancelAnimationFrame(frame)
      observer.disconnect()
    }
  }, [guildId, mode, variant, available])

  if (!available) return null

  return (
    <canvas
      ref={canvas}
      className={`spectrum spectrum-${variant} spectrum-${mode}`}
      aria-hidden="true"
    />
  )
}
