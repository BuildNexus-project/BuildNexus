import type { ProjectEvent } from './project-api'

/**
 * The events the Project Service publishes on `project-events`, exactly as the
 * envelope's `eventType` spells them.
 *
 * Deliberately not given friendly display names, unlike the project statuses.
 * These are wire values, and the one person who sees them — an administrator
 * checking whether an announcement got out — is matching them against what a
 * consumer subscribes to. "Project approved" would read more nicely and be
 * less useful.
 */
export const PROJECT_EVENT_TYPES = ['ProjectCreated', 'ProjectUpdated', 'ProjectApproved'] as const

export type ProjectEventType = (typeof PROJECT_EVENT_TYPES)[number]

/** Where one event stands with the broker. */
export type ProjectEventDelivery = 'delivered' | 'retrying' | 'waiting'

/**
 * How an event's delivery reads.
 *
 * `publishedAt` is the only field that says whether it got out. A pending event
 * that has already failed at least once is separated from one merely waiting
 * its turn, because the two need different reactions: the first is something to
 * look at, the second is the dispatcher not having reached it yet.
 */
export function deliveryOf(event: ProjectEvent): ProjectEventDelivery {
  if (event.publishedAt !== null) {
    return 'delivered'
  }

  return event.attemptCount > 0 ? 'retrying' : 'waiting'
}

/**
 * Whether anything here needs a human.
 *
 * Only an event that is both undelivered and has failed counts. A delivered
 * event that took several attempts is the outbox having done its job, and
 * flagging it would teach people to ignore the flag.
 */
export function needsAttention(events: readonly ProjectEvent[]): boolean {
  return events.some((event) => deliveryOf(event) === 'retrying')
}
