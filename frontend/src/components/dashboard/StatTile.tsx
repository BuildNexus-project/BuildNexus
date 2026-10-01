import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'

import { cn } from '@/lib/utils'

type StatTileProps = {
  label: string
  /** The figure. Whatever fits a tile: a count, a percentage, an amount. */
  value: ReactNode
  /** One line of context under the figure. */
  hint?: ReactNode
  /** Where the tile leads. A tile without one is a plain readout, not a link. */
  to?: string
  /** A colour hint beside the label, the way {@link StatusBadge} marks a status. */
  dot?: string
}

/**
 * `min-w-0` because a tile is a grid item, and a grid item will not shrink below the width of
 * its content unless told it may — so one long figure would otherwise stretch its column, and
 * on a phone the whole page, past the edge of the screen.
 */
const TILE_CLASSES = 'min-w-0 rounded-xl bg-card p-5 ring-1 ring-foreground/10'

/**
 * A figure longer than this many characters — an amount such as "LKR 4,810,000.00" — is set
 * smaller than a count, so it fits the tile instead of running out of it: smaller again on a
 * phone, where two tiles share the width and the number would otherwise be cut in half. A
 * count or a percentage stays as large as ever.
 */
const LONG_FIGURE = 8

/**
 * A figure's text with its non-breaking spaces made ordinary ones.
 *
 * A currency formatter puts a non-breaking space between the code and the number — "LKR 4,810,000.00"
 * — which is right in a sentence and wrong in a narrow tile: it leaves nowhere to break but inside
 * the digits, so the amount is cut in half. With an ordinary space the code drops to its own line
 * and the number stays whole.
 */
function breakable(text: string): string {
  return text.replace(/ /g, ' ')
}

/**
 * One figure on a dashboard, with the label that says what it counts.
 *
 * The label and the figure are always both on screen — a number with no name is a number
 * nobody can act on — and a tile that leads somewhere is a real link, so it can be
 * reached and read from the keyboard.
 */
export function StatTile({ label, value, hint, to, dot }: StatTileProps) {
  const content = (
    <>
      <span className="flex items-center gap-2 text-sm text-muted-foreground">
        {dot && <span aria-hidden className={cn('size-2 rounded-full', dot)} />}
        {label}
      </span>
      <span
        className={cn(
          'mt-2 block font-heading font-semibold tracking-tight tabular-nums break-words',
          typeof value === 'string' && value.length > LONG_FIGURE ? 'text-base sm:text-2xl' : 'text-4xl',
        )}
      >
        {typeof value === 'string' ? breakable(value) : value}
      </span>
      {hint && <span className="mt-1 block text-xs text-muted-foreground">{hint}</span>}
    </>
  )

  if (!to) {
    return <div className={TILE_CLASSES}>{content}</div>
  }

  return (
    <Link
      to={to}
      className={cn(
        TILE_CLASSES,
        'transition-all outline-none hover:-translate-y-0.5 hover:ring-foreground/20 focus-visible:ring-2 focus-visible:ring-ring',
      )}
    >
      {content}
    </Link>
  )
}
