/**
 * The project statuses, exactly as the Project Service spells them:
 * `Pending → Designing → DesignApproved → Construction → Completed` along the
 * forward path, plus `Cancelled` — a terminal state a project can be closed out
 * into before construction starts (US-08).
 */
export const PROJECT_STATUSES = [
  'Pending',
  'Designing',
  'DesignApproved',
  'Construction',
  'Completed',
  'Cancelled',
] as const

export type ProjectStatus = (typeof PROJECT_STATUSES)[number]

/**
 * Display names — the wire value `DesignApproved` reads badly in a UI.
 *
 * Only the labels live here. Which move is allowed from where is the service's
 * decision, and it tells us: a project's `allowedNextStatuses` says what it may
 * do next, so this app never has to keep its own copy of the transition table
 * and cannot drift from it.
 */
export const PROJECT_STATUS_LABELS: Record<ProjectStatus, string> = {
  Pending: 'Pending',
  Designing: 'Designing',
  DesignApproved: 'Design Approved',
  Construction: 'Construction',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
}
