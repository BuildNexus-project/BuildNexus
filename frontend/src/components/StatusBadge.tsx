import { Badge } from '@/components/ui/badge'
import { PROJECT_STATUS_LABELS, type ProjectStatus } from '@/lib/project-status'
import { cn } from '@/lib/utils'

/**
 * A dot for each status, so where a project stands can be read down a list
 * without reading every label. The label is still there and is what says it —
 * the dot is only ever a hint, never the only signal.
 *
 * The one place the app steps outside its black-and-white palette, because a
 * status is the one thing here that is genuinely categorical.
 */
const DOT_CLASSES: Record<ProjectStatus, string> = {
  Pending: 'bg-amber-500',
  Designing: 'bg-sky-500',
  DesignApproved: 'bg-violet-500',
  Construction: 'bg-orange-500',
  Completed: 'bg-emerald-500',
  Cancelled: 'bg-red-500',
}

export function StatusBadge({ status, className }: { status: ProjectStatus; className?: string }) {
  return (
    <Badge variant="secondary" className={cn('gap-1.5', className)}>
      <span aria-hidden className={cn('size-1.5 rounded-full', DOT_CLASSES[status])} />
      {PROJECT_STATUS_LABELS[status]}
    </Badge>
  )
}
