import type { ReactNode } from 'react'

import {
  DESIGN_STATES,
  type DesignState,
  type InvoiceDue,
  type PendingRevision,
  type ProjectBuildProgress,
  type ProjectDesignStatus,
} from './dashboard-api'
import type { SliceState } from './use-dashboard-slice'

/*
 * What the role dashboards do with the slices once they have them (US-21): joining on the
 * project id, summing, and formatting. Kept apart from the components so each rule is
 * stated once and tested without rendering anything.
 */

/**
 * What to put where a slice's figure goes: the figure once it has arrived, an ellipsis
 * while it is on its way, and a dash if it never will — so a tile is never blank and a
 * failed slice is never mistaken for a zero.
 */
export function figureOf<T>(state: SliceState<T>, pick: (data: T) => ReactNode): ReactNode {
  if (state.status === 'ready') {
    return pick(state.data)
  }

  return state.status === 'loading' ? '…' : '—'
}

/** Money as a reader would write it. The rest of the app prices in LKR. */
export function formatMoney(amount: number): string {
  return amount.toLocaleString(undefined, {
    style: 'currency',
    currency: 'LKR',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
}

/** A calendar date as a reader would write it — no time, since none of these answers need one. */
export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

const MS_PER_DAY = 24 * 60 * 60 * 1000

/**
 * Whole days from `iso` to `now`, never negative.
 *
 * A timestamp a moment in the future — a service clock a little ahead of the browser's —
 * is "0 days", not "-1": nobody has waited a negative time.
 */
export function daysSince(iso: string, now: number = Date.now()): number {
  return Math.max(0, Math.floor((now - new Date(iso).getTime()) / MS_PER_DAY))
}

/** The first eight characters of an id — enough to tell two apart when no name is known. */
export function shortId(id: string): string {
  return id.slice(0, 8)
}

/**
 * A lookup from project id to project name, for pages that hold ids from one service and
 * names from another.
 *
 * A project whose name is not known — the caller is not on it, or the names could not be
 * read — falls back to its short id rather than to nothing, so a row is never blank.
 */
export function projectNamer(projects: readonly { id: string; name: string }[]): (id: string) => string {
  const names = new Map(projects.map((project) => [project.id, project.name]))

  return (id) => names.get(id) ?? shortId(id)
}

/** Items keyed by one of their fields, for the join between two slices. Later items win a repeated key. */
export function indexBy<T>(items: readonly T[], key: (item: T) => string): Map<string, T> {
  return new Map(items.map((item) => [key(item), item]))
}

/**
 * How much of a Client's build work is done across every project that has a plan —
 * weighted by milestones, so a 10-milestone project counts for more than a 2-milestone
 * one, which an average of the two percentages would get wrong.
 *
 * `null` when there is nothing planned to measure. That is not 0%: nothing done out of
 * nothing planned is "no plan yet", and showing zero would read as a stalled build.
 */
export function overallProgress(
  projects: readonly ProjectBuildProgress[],
): { percent: number; completed: number; total: number } | null {
  const total = projects.reduce((sum, project) => sum + project.totalMilestones, 0)

  if (total === 0) {
    return null
  }

  const completed = projects.reduce((sum, project) => sum + project.completedMilestones, 0)

  return { percent: Math.round((completed / total) * 100), completed, total }
}

/** How many projects are in each design state, with every state present so a count is never `undefined`. */
export function designStateCounts(projects: readonly ProjectDesignStatus[]): Record<DesignState, number> {
  const counts = Object.fromEntries(DESIGN_STATES.map((state) => [state, 0])) as Record<DesignState, number>

  for (const project of projects) {
    counts[project.state] += 1
  }

  return counts
}

/**
 * What is still owed on each project, summed across its unsettled invoices.
 *
 * Summed in whole cents and turned back: adding decimal amounts as floating point would
 * let a total drift by a fraction of a cent, and a figure that does not match the invoice
 * it came from is one nobody trusts.
 */
export function outstandingByProject(invoices: readonly InvoiceDue[]): Map<string, number> {
  const cents = new Map<string, number>()

  for (const invoice of invoices) {
    cents.set(invoice.projectId, (cents.get(invoice.projectId) ?? 0) + Math.round(invoice.outstandingAmount * 100))
  }

  return new Map([...cents].map(([projectId, total]) => [projectId, total / 100]))
}

/** How many pending revisions each project has, for a project that has none to be absent. */
export function revisionCountsByProject(revisions: readonly PendingRevision[]): Map<string, number> {
  const counts = new Map<string, number>()

  for (const revision of revisions) {
    counts.set(revision.projectId, (counts.get(revision.projectId) ?? 0) + 1)
  }

  return counts
}
