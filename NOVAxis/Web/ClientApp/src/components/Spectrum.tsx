import { useEffect, useRef } from 'react'

import { useSpectrumRequest } from '../player'
import { readSpectrum, useSpectrumFeed } from '../spectrum'

interface SpectrumProps {
  guildId: string | null
  /** False where the backend cannot produce one at all, so nothing is asked for. */
  available: boolean
}

/** Height of the strip behind the hero, in CSS pixels. */
const Height = 120

/**
 * Quiet to loud, through the two colours this interface already uses. The cold end is the
 * page's own background rather than a colour of its own: silence should read as nothing,
 * and a spectrum whose quiet half glows is mostly glow.
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
 * What the bot is playing, drawn behind the now-playing card from bands the server
 * computes and pushes. The page has no audio of its own, so this is the only way it can
 * know - see SpectrumAnalyzer for where the numbers come from.
 */
export function Spectrum({ guildId, available }: SpectrumProps) {
  const canvas = useRef<HTMLCanvasElement>(null)
  const { requestSpectrum, live } = useSpectrumRequest()

  useSpectrumFeed(guildId, requestSpectrum, available && live)

  useEffect(() => {
    const element = canvas.current

    if (!element || !available) return

    const context = element.getContext('2d')

    if (!context) return

    let bars: Float32Array | null = null
    let peaks: Float32Array | null = null
    let frame = 0

    const resize = () => {
      const ratio = Math.min(window.devicePixelRatio || 1, 2)
      const width = element.clientWidth || 1

      element.width = Math.floor(width * ratio)
      element.height = Math.floor(Height * ratio)
      element.style.height = `${Height}px`

      context.setTransform(ratio, 0, 0, ratio, 0, 0)
    }

    resize()

    const observer = new ResizeObserver(resize)
    observer.observe(element)

    const draw = () => {
      frame = requestAnimationFrame(draw)

      const width = element.clientWidth || 1
      const latest = readSpectrum(guildId)

      context.clearRect(0, 0, width, Height)

      if (!latest) {
        // Nothing arriving: let the picture go rather than freeze on the last thing heard
        bars?.fill(0)
        peaks?.fill(0)
        return
      }

      const { bands } = latest
      const count = bands.length

      if (!bars || !peaks || bars.length !== count) {
        bars = new Float32Array(count)
        peaks = new Float32Array(count)
      }

      const gap = width / count
      const thickness = Math.max(gap - 2, 1)

      for (let i = 0; i < count; i++) {
        const value = bands[i] / 255

        // Straight up, slow down: a bar which fell as fast as it rose would flicker
        bars[i] = Math.max(value, bars[i] * 0.86)
        peaks[i] = Math.max(bars[i], peaks[i] - 0.008)

        const tall = bars[i] * Height

        context.fillStyle = palette(bands[i])
        context.fillRect(i * gap, Height - tall, thickness, tall)

        const peak = peaks[i] * Height

        if (peak > 2) {
          context.fillStyle = 'rgba(233, 238, 246, 0.55)'
          context.fillRect(i * gap, Height - peak - 1, thickness, 1.5)
        }
      }
    }

    frame = requestAnimationFrame(draw)

    return () => {
      cancelAnimationFrame(frame)
      observer.disconnect()
    }
  }, [guildId, available])

  if (!available) return null

  return <canvas ref={canvas} className="spectrum" aria-hidden="true" />
}
