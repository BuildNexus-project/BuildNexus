import type { SliceState } from '@/lib/use-dashboard-slice'

/**
 * Says so when a slice of a dashboard could not be loaded — once each, naming the slice
 * and giving the reason the service gave, if it gave one.
 *
 * A dashboard is several requests to several services and they fail separately. So a
 * failure is stated beside what did arrive, not instead of it: the figures that loaded
 * stay on screen, the ones that did not show a dash, and this is what explains the dash.
 */
export function SliceProblems({ slices }: { slices: readonly SliceState<unknown>[] }) {
  const messages = slices.flatMap((slice) =>
    slice.status === 'error' ? [[slice.message, slice.detail].filter(Boolean).join(' ')] : [],
  )

  if (messages.length === 0) {
    return null
  }

  return (
    <div className="flex flex-col gap-1">
      {messages.map((message, index) => (
        <p key={`${index}-${message}`} role="alert" className="text-destructive text-sm">
          {message}
        </p>
      ))}
    </div>
  )
}
