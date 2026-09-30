import { describe, expect, it } from 'vitest'

import type {
  InvoiceDue,
  PendingRevision,
  ProjectBuildProgress,
  ProjectDesignStatus,
} from './dashboard-api'
import {
  daysSince,
  designStateCounts,
  figureOf,
  formatDate,
  formatMoney,
  indexBy,
  outstandingByProject,
  overallProgress,
  projectNamer,
  revisionCountsByProject,
  shortId,
} from './dashboard-view'

/**
 * What the role dashboards do with the slices once they have them (US-21): the joins,
 * sums and formatting. Pure functions, so each rule is pinned without rendering a page.
 */

const VILLA = '11111111-2222-4333-8444-555555555555'
const COTTAGE = '66666666-7777-4888-8999-000000000000'

function build(projectId: string, completed: number, total: number): ProjectBuildProgress {
  return {
    projectId,
    phaseStatus: 'Started',
    totalMilestones: total,
    completedMilestones: completed,
    progressPercent: total === 0 ? 0 : (completed / total) * 100,
  }
}

function design(projectId: string, state: ProjectDesignStatus['state']): ProjectDesignStatus {
  return {
    projectId,
    state,
    documentCount: 1,
    awaitingReviewCount: 0,
    revisionRequestedCount: 0,
    approvedCount: 0,
  }
}

function invoice(projectId: string, outstandingAmount: number): InvoiceDue {
  return {
    invoiceId: `invoice-${projectId}-${outstandingAmount}`,
    projectId,
    amount: outstandingAmount,
    amountPaid: 0,
    outstandingAmount,
    raisedAt: '2026-09-01T09:00:00',
  }
}

function revision(projectId: string): PendingRevision {
  return {
    projectId,
    documentId: `document-${projectId}`,
    documentName: 'Elevations',
    versionNumber: 1,
    reviewComment: null,
    requestedAt: '2026-09-01T09:00:00',
  }
}

describe('figureOf', () => {
  const pick = (data: { count: number }) => data.count

  it('is the figure once the slice has arrived', () => {
    expect(figureOf({ status: 'ready', data: { count: 7 } }, pick)).toBe(7)
  })

  it('keeps a real zero as zero', () => {
    // A failed slice must never look like nothing to report, nor nothing like a failure.
    expect(figureOf({ status: 'ready', data: { count: 0 } }, pick)).toBe(0)
  })

  it('is an ellipsis while the slice is on its way', () => {
    expect(figureOf({ status: 'loading' }, pick)).toBe('…')
  })

  it('is a dash, not a zero, when the slice failed', () => {
    expect(figureOf({ status: 'error', message: 'Boom' }, pick)).toBe('—')
  })
})

describe('overallProgress', () => {
  it('weights each project by its milestones rather than averaging the percentages', () => {
    // 1 of 10 is 10% and 2 of 2 is 100%; their average is 55%, but 3 of 12 milestones is done.
    const progress = overallProgress([build(VILLA, 1, 10), build(COTTAGE, 2, 2)])

    expect(progress).toEqual({ percent: 25, completed: 3, total: 12 })
  })

  it('rounds to a whole percent', () => {
    expect(overallProgress([build(VILLA, 1, 3)])?.percent).toBe(33)
    expect(overallProgress([build(VILLA, 2, 3)])?.percent).toBe(67)
  })

  it('is null, not zero, when nothing is planned', () => {
    // Zero would read as a stalled build; there is simply no plan to measure yet.
    expect(overallProgress([])).toBeNull()
    expect(overallProgress([build(VILLA, 0, 0)])).toBeNull()
  })

  it('is zero when there is a plan and none of it is done', () => {
    expect(overallProgress([build(VILLA, 0, 5)])).toEqual({ percent: 0, completed: 0, total: 5 })
  })
})

describe('designStateCounts', () => {
  it('counts the projects in each state', () => {
    const counts = designStateCounts([
      design(VILLA, 'AwaitingReview'),
      design(COTTAGE, 'AwaitingReview'),
      design('a', 'Approved'),
      design('b', 'NoDesign'),
    ])

    expect(counts).toEqual({ NoDesign: 1, AwaitingReview: 2, RevisionRequested: 0, Approved: 1 })
  })

  it('has every state present, at zero, for no projects', () => {
    expect(designStateCounts([])).toEqual({
      NoDesign: 0,
      AwaitingReview: 0,
      RevisionRequested: 0,
      Approved: 0,
    })
  })
})

describe('outstandingByProject', () => {
  it('sums what is owed across a projects invoices', () => {
    const owed = outstandingByProject([invoice(VILLA, 500), invoice(VILLA, 250), invoice(COTTAGE, 100)])

    expect(owed.get(VILLA)).toBe(750)
    expect(owed.get(COTTAGE)).toBe(100)
  })

  it('leaves a project that owes nothing out rather than at zero', () => {
    expect(outstandingByProject([invoice(VILLA, 500)]).has(COTTAGE)).toBe(false)
  })

  it('does not let decimal amounts drift', () => {
    // 0.1 + 0.2 is 0.30000000000000004 as floating point; a total must match its invoices.
    const owed = outstandingByProject([invoice(VILLA, 0.1), invoice(VILLA, 0.2)])

    expect(owed.get(VILLA)).toBe(0.3)
  })
})

describe('revisionCountsByProject', () => {
  it('counts the revisions on each project', () => {
    const counts = revisionCountsByProject([revision(VILLA), revision(VILLA), revision(COTTAGE)])

    expect(counts.get(VILLA)).toBe(2)
    expect(counts.get(COTTAGE)).toBe(1)
  })

  it('leaves a project with none out', () => {
    expect(revisionCountsByProject([revision(VILLA)]).has(COTTAGE)).toBe(false)
  })
})

describe('projectNamer', () => {
  it('names a project it knows', () => {
    const name = projectNamer([{ id: VILLA, name: 'Beachfront villa' }])

    expect(name(VILLA)).toBe('Beachfront villa')
  })

  it('falls back to a short id for a project it does not, rather than to nothing', () => {
    const name = projectNamer([{ id: VILLA, name: 'Beachfront villa' }])

    expect(name(COTTAGE)).toBe('66666666')
  })
})

describe('indexBy', () => {
  it('keys items by the field given', () => {
    const byProject = indexBy([design(VILLA, 'Approved'), design(COTTAGE, 'NoDesign')], (d) => d.projectId)

    expect(byProject.get(COTTAGE)?.state).toBe('NoDesign')
    expect(byProject.size).toBe(2)
  })
})

describe('shortId', () => {
  it('is the first eight characters', () => {
    expect(shortId(VILLA)).toBe('11111111')
  })
})

describe('daysSince', () => {
  const now = new Date('2026-09-10T12:00:00').getTime()

  it('counts whole days', () => {
    expect(daysSince('2026-09-07T12:00:00', now)).toBe(3)
    expect(daysSince('2026-09-07T13:00:00', now)).toBe(2)
  })

  it('is zero on the same day', () => {
    expect(daysSince('2026-09-10T08:00:00', now)).toBe(0)
  })

  it('is never negative when the timestamp is ahead of the clock', () => {
    expect(daysSince('2026-09-12T12:00:00', now)).toBe(0)
  })
})

describe('formatMoney', () => {
  it('writes an amount in rupees to two decimal places', () => {
    const written = formatMoney(1250)

    expect(written).toMatch(/1,250\.00/)
    expect(written).toMatch(/LKR|Rs/)
  })

  it('writes zero as an amount rather than as nothing', () => {
    expect(formatMoney(0)).toMatch(/0\.00/)
  })
})

describe('formatDate', () => {
  it('writes the day, month and year without a time', () => {
    const written = formatDate('2026-09-01T09:00:00')

    expect(written).toMatch(/2026/)
    expect(written).not.toMatch(/09:00|9:00/)
  })
})
