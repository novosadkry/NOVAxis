import { SpectrumMode, SpectrumVariant, useSpectrumPrefs } from '../spectrum'
import { Spectrum } from './Spectrum'

const modes: { value: SpectrumMode; label: string }[] = [
  { value: 'waterfall', label: 'Spektrogram' },
  { value: 'bars', label: 'Sloupce' },
]

const places: { value: SpectrumVariant; label: string }[] = [
  { value: 'panel', label: 'Panel' },
  { value: 'ambient', label: 'Za obalem' },
  { value: 'bar', label: 'V liště' },
]

/**
 * The spectrum's own card, and the controls for it.
 *
 * The placement switch is here on purpose and temporarily: which of the three looks best
 * is not a thing to settle by imagining it, so all three are wired and the choice is one
 * click. Once one has won, the other two and this switch go.
 */
export function SpectrumPanel({ guildId, available }: { guildId: string; available: boolean }) {
  const { mode, variant, set } = useSpectrumPrefs()

  if (!available) return null

  return (
    <section className="panel spectrum-card">
      <header className="queue-head">
        <h3 className="eyebrow">SPEKTRUM</h3>

        <div className="spectrum-switch">
          {modes.map(option => (
            <button
              key={option.value}
              type="button"
              className={'text-btn' + (mode === option.value ? ' active' : '')}
              onClick={() => set({ mode: option.value })}
            >
              {option.label}
            </button>
          ))}

          <span className="spectrum-switch-sep" aria-hidden="true" />

          {places.map(option => (
            <button
              key={option.value}
              type="button"
              className={'text-btn' + (variant === option.value ? ' active' : '')}
              onClick={() => set({ variant: option.value })}
            >
              {option.label}
            </button>
          ))}
        </div>
      </header>

      {variant === 'panel' ? (
        <Spectrum guildId={guildId} mode={mode} variant="panel" available={available} />
      ) : (
        <p className="empty-note">
          Spektrum je teď {variant === 'ambient' ? 'za obalem skladby' : 'v liště přehrávače'}.
        </p>
      )}
    </section>
  )
}
