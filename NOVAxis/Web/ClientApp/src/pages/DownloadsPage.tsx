import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'

import { DownloadDto, DownloadFormatDto, DownloadProbeDto, api, isAbortError } from '../api'
import { describeFailure, describeFileError, describeFreed, useDownloads } from '../downloads'
import { useGuilds } from '../guilds'
import { usePlayerState } from '../player'
import { formatBytes, formatDuration } from '../format'
import { Close, Download, Note } from '../Icons'
import { useToast } from '../Toast'
import { AppShell } from '../components/AppShell'
import { PlayerBar } from '../components/PlayerBar'

const STATE_LABELS: Record<DownloadDto['state'], string> = {
  Pending: 'PŘIPRAVUJI',
  Running: 'PŘIPRAVUJI',
  Ready: 'PŘIPRAVENO',
  Failed: 'NEPOVEDLO SE',
  Revoked: 'ZRUŠENO',
  Expired: 'VYPRŠELO',
}

/** A local deadline, so the countdown never subtracts the server's clock from ours. */
function useRemaining(expiresAt: number | undefined, sampledAt: number | undefined): number | null {
  const deadline = useMemo(
    () => (expiresAt && sampledAt ? Date.now() + (expiresAt - sampledAt) : null),
    [expiresAt, sampledAt],
  )

  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    if (deadline === null) return
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [deadline])

  return deadline === null ? null : Math.max(0, deadline - now)
}

/**
 * Downloads, in the same frame as everything else: the guilds stay down the side and the
 * transport keeps playing, so this is somewhere you look rather than somewhere you go.
 */
export function DownloadsPage() {
  // Downloads belong to the person, not to a guild - but the page still sits in the
  // frame of whichever guild was last open, so the transport carries on playing. There
  // is none when the page was reached cold, from a link handed out on Discord
  const { lastGuildId: guildId } = useGuilds()
  const live = usePlayerState(guildId)

  const { toast, run } = useToast()
  const [params, setParams] = useSearchParams()
  const { overview, error: liveError, reload } = useDownloads()

  const [url, setUrl] = useState(() => params.get('url') ?? '')
  const [probe, setProbe] = useState<DownloadProbeDto | null>(null)
  const [probing, setProbing] = useState(false)
  const [probeError, setProbeError] = useState<string | null>(null)
  const [formatId, setFormatId] = useState<string | null>(null)
  const [starting, setStarting] = useState(false)

  const pending = useRef<AbortController | null>(null)
  const greeted = useRef(false)

  const downloads = overview?.downloads ?? []
  const storage = overview?.storage ?? null
  const quota = overview?.quota ?? null

  const exhausted = quota !== null && quota.remaining <= 0

  // A link that failed comes back as ?error=, because the file endpoint cannot toast
  useEffect(() => {
    if (greeted.current) return
    greeted.current = true

    const code = params.get('error')
    if (code) toast(describeFileError(code))

    const seeded = params.get('url')
    if (seeded) void lookUp(seeded)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function lookUp(target: string) {
    const trimmed = target.trim()
    if (!trimmed) return

    pending.current?.abort()
    const controller = new AbortController()
    pending.current = controller

    setProbing(true)
    setProbeError(null)

    try {
      const found = await api.probeDownload(trimmed, controller.signal)
      if (controller.signal.aborted) return

      setProbe(found)
      setFormatId(found.formats.find(f => f.withinLimit)?.id ?? null)
    } catch (failure) {
      if (isAbortError(failure)) return
      setProbe(null)
      setProbeError(describeFailure(failure))
    } finally {
      if (!controller.signal.aborted) setProbing(false)
    }
  }

  async function start() {
    if (!probe || !formatId) return

    const chosen = probe.formats.find(f => f.id === formatId)
    if (!chosen) return

    setStarting(true)

    try {
      const { freed } = await api.startDownload(probe.url, chosen.kind, chosen.id, probe.title)
      reload()

      // Room comes out of their own oldest links, and a link that stopped working
      // without a word is worse than the wait
      if (freed.length > 0) toast(describeFreed(freed))
    } catch (failure) {
      toast(describeFailure(failure))
    } finally {
      setStarting(false)
    }
  }

  return (
    <AppShell
      activeGuildId={guildId ?? undefined}
      activeTool="downloads"
      title="Stahování"
      subtitle={`max 100 MB na soubor · platí hodinu · ${storage ? formatBytes(storage.limitBytes) : "300 MB"} prostoru`}
      backTo={guildId ? `/g/${guildId}` : '/'}
      bar={guildId ? <PlayerBar guildId={guildId} live={live} /> : undefined}
    >
      {liveError && <p className="empty-note">{liveError}</p>}

      <section className="panel">
        <form
          className="download-form"
          onSubmit={event => {
            event.preventDefault()
            void lookUp(url)
          }}
        >
          <div className="search-field">
            <Download size={16} />
            <input
              value={url}
              onChange={event => {
                setUrl(event.target.value)
                setParams(
                  previous => {
                    const next = new URLSearchParams(previous)
                    next.delete('error')
                    return next
                  },
                  { replace: true },
                )
              }}
              placeholder="Vlož odkaz na video, gif nebo skladbu…"
              aria-label="Odkaz ke stažení"
              spellCheck={false}
            />
            {probing && <span className="search-spinner" aria-label="Načítám" />}
          </div>
          <button type="submit" className="btn-solid" disabled={probing || !url.trim()}>
            Načíst formáty
          </button>
        </form>

        {quota && (
          <p className={`download-quota${exhausted ? ' warn' : ''}`}>
            {exhausted
              ? `vyčerpáno · obnova ${
                  quota.resetsAt
                    ? new Date(quota.resetsAt).toLocaleTimeString('cs-CZ', {
                        hour: '2-digit',
                        minute: '2-digit',
                      })
                    : 'později'
                }`
              : `zbývá ${quota.remaining} / ${quota.limit} za hodinu`}
          </p>
        )}

        {probeError && <p className="empty-note search-failure">{probeError}</p>}
      </section>

      {probe && (
        <section className="panel">
          <div className="download-found">
            <div className="hero-art small">
              {probe.thumbnailUrl ? (
                <img src={probe.thumbnailUrl} alt="" />
              ) : (
                <div className="hero-art-fallback">
                  <Note size={28} />
                </div>
              )}
            </div>
            <div>
              <h2>{probe.title}</h2>
              <p className="download-hint">
                {formatDuration(probe.durationMs, probe.isLiveStream)}
              </p>
            </div>
          </div>

          <FormatPicker formats={probe.formats} selected={formatId} onSelect={setFormatId} />

          <div className="download-actions">
            <button
              type="button"
              className="btn-solid"
              disabled={!formatId || starting || exhausted}
              onClick={() => void start()}
            >
              {exhausted ? 'Limit vyčerpán' : starting ? 'Spouštím…' : 'Stáhnout'}
            </button>
          </div>
        </section>
      )}

      {downloads.length > 0 && (
        <section className="panel">
          <header className="queue-head">
            <h3 className="eyebrow">ODKAZY · {downloads.length}</h3>
            {storage && (
              <span className={`download-space${storage.usedBytes / storage.limitBytes > 0.8 ? ' warn' : ''}`}>
                {formatBytes(storage.usedBytes)} / {formatBytes(storage.limitBytes)}
              </span>
            )}
          </header>

          {storage && (
            <div className="progress-line inline space-meter">
              <div
                className="progress-fill"
                style={{
                  width: `${Math.min(100, Math.round((storage.usedBytes / storage.limitBytes) * 100))}%`,
                }}
              />
            </div>
          )}

          <div className="download-list">
            {downloads.map(download => (
              <DownloadCard
                key={download.id}
                download={download}
                onRevoke={() => void run(api.revokeDownload(download.id).then(reload))}
              />
            ))}
          </div>
        </section>
      )}

    </AppShell>
  )
}

interface DownloadCardProps {
  download: DownloadDto
  onRevoke: () => void
}

/**
 * One prepared link: what it is, how it is getting on, and how long it has left.
 */
function DownloadCard({ download, onRevoke }: DownloadCardProps) {
  const remaining = useRemaining(download.expiresAt, download.sampledAt)
  const running = download.state === 'Pending' || download.state === 'Running'

  return (
    <div className="download-item">
      <div className="download-active">
        <div>
          <p className="eyebrow accent">{STATE_LABELS[download.state]}</p>
          <h2>{download.title}</h2>
          <p className="download-hint">{download.formatLabel}</p>
        </div>
        <button
          type="button"
          className="icon-btn"
          aria-label={`Zrušit ${download.title}`}
          title="Zrušit"
          onClick={onRevoke}
        >
          <Close size={16} />
        </button>
      </div>

      {running && (
        <>
          <div className="progress-line inline">
            <div
              className="progress-fill"
              style={{ width: `${Math.round((download.progress ?? 0) * 100)}%` }}
            />
          </div>
          <p className="download-readout">
            {formatBytes(download.receivedBytes)}
            {download.sizeBytes ? ` / ${formatBytes(download.sizeBytes)}` : ''}
            {download.progress !== null ? ` · ${Math.round(download.progress * 100)} %` : ''}
          </p>
        </>
      )}

      {download.state === 'Ready' && download.fileUrl && (
        <div className="download-actions">
          <a className="btn-solid" href={download.fileUrl} download={download.fileName ?? undefined}>
            <Download size={16} /> Stáhnout soubor
          </a>
          <span className="download-readout">{formatBytes(download.sizeBytes)}</span>
          <span
            className={`download-expiry${remaining !== null && remaining < 300000 ? ' urgent' : ''}`}
          >
            {remaining !== null && remaining > 0
              ? `platí ještě ${formatDuration(remaining)}`
              : 'odkaz vypršel'}
          </span>
        </div>
      )}

      {download.state === 'Failed' && (
        <p className="download-error">{download.error ?? 'Stahování se nezdařilo'}</p>
      )}
    </div>
  )
}

interface FormatPickerProps {
  formats: DownloadFormatDto[]
  selected: string | null
  onSelect: (id: string) => void
}

function FormatPicker({ formats, selected, onSelect }: FormatPickerProps) {
  const groups: { kind: DownloadFormatDto['kind']; label: string }[] = [
    { kind: 'Video', label: 'VIDEO' },
    { kind: 'Audio', label: 'ZVUK' },
  ]

  return (
    <>
      {groups.map(group => {
        const rows = formats.filter(f => f.kind === group.kind)
        if (rows.length === 0) return null

        return (
          <div className="format-group" key={group.kind}>
            <p className="eyebrow">{group.label}</p>
            <div className="format-list">
              {rows.map(format => (
                <button
                  key={`${format.kind}:${format.id}`}
                  type="button"
                  className={`format-row${format.id === selected ? ' selected' : ''}`}
                  disabled={!format.withinLimit}
                  onClick={() => onSelect(format.id)}
                >
                  <span>{format.label}</span>
                  <span className="format-size">
                    {format.sizeBytes === null ? '~ neznámá' : formatBytes(format.sizeBytes)}
                  </span>
                  {!format.withinLimit && <span className="format-too-large">nad limit</span>}
                </button>
              ))}
            </div>
          </div>
        )
      })}
    </>
  )
}
