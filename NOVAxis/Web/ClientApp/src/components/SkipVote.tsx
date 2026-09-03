import { api, SkipVoteDto } from '../api'
import { useUser } from '../user'
import { useToast } from '../Toast'
import { Skip } from '../Icons'

interface SkipVoteProps {
  guildId: string
  vote: SkipVoteDto | null
}

/**
 * The guild's open skip vote, the same one Discord is looking at. It arrives in the
 * ordinary state snapshot rather than through a channel of its own, so it appears here
 * within a second of anyone opening it, wherever they opened it from.
 */
export function SkipVote({ guildId, vote }: SkipVoteProps) {
  const { run } = useToast()
  const user = useUser()

  if (!vote) return null

  const mine = user?.id
  const forIt = mine != null && vote.favourIds.includes(mine)
  const against = mine != null && vote.againstIds.includes(mine)

  const missing = Math.max(vote.needed - vote.inFavour, 0)
  const progress = vote.needed > 0 ? Math.min(vote.inFavour / vote.needed, 1) : 0

  return (
    <section className="panel skipvote" aria-live="polite">
      <header className="queue-head">
        <h3 className="eyebrow accent">
          <Skip size={14} />
          HLASOVÁNÍ O PŘESKOČENÍ
        </h3>
        <span className="queue-total">
          {vote.inFavour}/{vote.needed} · poslouchá {vote.listeners}
        </span>
      </header>

      {vote.title && <p className="skipvote-track">{vote.title}</p>}

      <div className="progress-line inline skipvote-meter" role="presentation">
        <div className="progress-fill" style={{ width: `${progress * 100}%` }} />
      </div>

      <div className="skipvote-actions">
        <p className="skipvote-note">
          {missing === 0
            ? 'Hlasování prošlo.'
            : `Chybí ${missing} ${missing === 1 ? 'hlas' : missing < 5 ? 'hlasy' : 'hlasů'}.`}
        </p>

        <button
          type="button"
          className={'btn-ghost' + (forIt ? ' active' : '')}
          disabled={forIt}
          onClick={() => run(api.skip(guildId).then(() => undefined))}
        >
          {forIt ? 'Hlasoval jsi pro' : 'Přeskočit'}
        </button>

        <button
          type="button"
          className={'btn-ghost' + (against ? ' active' : '')}
          disabled={against}
          onClick={() => run(api.voteAgainstSkip(guildId).then(() => undefined))}
        >
          {against ? 'Hlasoval jsi proti' : 'Nechat hrát'}
        </button>
      </div>
    </section>
  )
}
