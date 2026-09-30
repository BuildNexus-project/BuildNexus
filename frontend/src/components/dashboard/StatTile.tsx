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

const TILE_CLASSES = 'rounded-xl bg-card p-5 ring-1 ring-foreground/10'

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
      <span className="mt-2 block font-heading text-4xl font-semibold tracking-tight tabular-nums">
        {value}
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
