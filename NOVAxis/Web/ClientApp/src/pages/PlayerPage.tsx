import { useCallback, useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'

import { api, PlayResponse, TrackDto } from '../api'
import { useGuilds } from '../guilds'
import { usePendingTracks } from '../pending'
import { usePlayerState } from '../player'
import { describeFailure, useDownloads } from '../downloads'
import { useToast } from '../Toast'
import { AppShell } from '../components/AppShell'
import { NowPlaying } from '../components/NowPlaying'
import { PlayerBar } from '../components/PlayerBar'
import { QueueList } from '../components/QueueList'
import { SkipVote } from '../components/SkipVote'
import { SearchBox } from '../components/SearchBox'

/**
 * What the core is playing: the now-playing hero, the queue and the transport bar.
 * Everything here redraws from one live state.
 */
export function PlayerPage() {
  const { guildId = '' } = useParams()
  const live = usePlayerState(guildId)

  // So the downloads page, which has no guild of its own, keeps this one's frame
  const { remember } = useGuilds()

  useEffect(() => {
    if (guildId) remember(guildId)
  }, [guildId, remember])

  const { reload } = useDownloads()
  const { toast } = useToast()
  const [starting, setStarting] = useState<string | null>(null)

  const quickDownload = useCallback(
    async (track: TrackDto) => {
      if (!track.uri || starting) return

      setStarting(track.uri)

      try {
        await api.startDownload(track.uri, 'Audio', '', track.title)
        reload()

        toast(`Stahuji „${track.title}“`)
      } catch (error) {
        toast(describeFailure(error))
      } finally {
        setStarting(null)
      }
    },
    [reload, starting, toast],
  )

  const state = live.state
  const { pending, expect } = usePendingTracks(live.receivedAt)

  // With nothing playing, the track being fetched is the one about to play, so it
  // stands in the hero rather than at the head of a queue it will never join
  const onEnqueue = useCallback(
    (label: string, known: TrackDto | null, request: Promise<PlayResponse>) =>
      expect(label, known, request, !state?.current),
    [expect, state?.current],
  )

  const heroPending = pending.find(entry => entry.hero) ?? null
  const queuePending = pending.filter(entry => !entry.hero)

  return (
    <AppShell
      activeGuildId={guildId}
      subtitle={
        state?.connected && state.voiceChannel
          ? `připojeno · ${state.voiceChannel.name}`
          : 'jádro v klidovém režimu'
      }
      actions={<SearchBox guildId={guildId} onEnqueue={onEnqueue} />}
      bar={<PlayerBar guildId={guildId} live={live} />}
    >
      {live.error && <p className="empty-note">{live.error}</p>}

      {!live.error && (
        <>
          <NowPlaying
            live={live}
            onDownload={quickDownload}
            startingUri={starting}
            pending={heroPending}
          />
          <SkipVote guildId={guildId} vote={state?.skipVote ?? null} />
          <QueueList
            guildId={guildId}
            state={state}
            onDownload={quickDownload}
            startingUri={starting}
            pending={queuePending}
          />
        </>
      )}
    </AppShell>
  )
}
