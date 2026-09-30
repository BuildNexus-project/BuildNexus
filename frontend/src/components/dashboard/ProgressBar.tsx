/**
 * A horizontal bar showing how far along something is, with the figure beside it.
 *
 * The bar is decoration for the eye; the `progressbar` role and its value are what say it
 * to everything else, and the percentage is written out beside it so it can be read
 * without judging a length.
 */
export function ProgressBar({ percent, label }: { percent: number; label: string }) {
  const clamped = Math.min(100, Math.max(0, percent))

  return (
    <div className="flex items-center gap-2">
      <div
        role="progressbar"
        aria-valuenow={Math.round(clamped)}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={label}
        className="bg-muted h-2 w-24 shrink-0 overflow-hidden rounded-full"
      >
        <div
          className="bg-primary h-full transition-[width] duration-500 ease-out"
          style={{ width: `${clamped}%` }}
        />
      </div>
      <span className="text-sm tabular-nums">{Math.round(clamped)}%</span>
    </div>
  )
}
