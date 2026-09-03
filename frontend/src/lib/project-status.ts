/**
 * The project lifecycle, exactly as the Project Service spells it, in the order
 * a project moves through it:
 * `Pending → Designing → DesignApproved → Construction → Completed`.
 */
export const PROJECT_STATUSES = [
  'Pending',
  'Designing',
  'DesignApproved',
  'Construction',
  'Completed',
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
}
