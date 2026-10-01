import type { MilestoneStatus } from './construction-api'

/*
 * A milestone's due date is a calendar day — `yyyy-MM-dd` on the wire, no time of day and no
 * timezone — so it is handled here as that string, not as a `Date` that would bring a
 * timezone with it. Sorting and comparing the strings is chronological, because the format
 * puts the largest unit first.
 */

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

/**
 * Today's date in the reader's own timezone, as `yyyy-MM-dd`.
 *
 * Local, not UTC: a Project Manager in Colombo deciding whether the 5th has passed means
 * their 5th. (The dashboard's overdue figures are the service's, judged against the UTC day
 * — the two can differ for the few hours either side of midnight, and each is right about
 * its own question.)
 */
export function todayLocal(now: Date = new Date()): string {
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/** Whether `value` is a real `yyyy-MM-dd` day — `2026-02-30` is not, however well-formed. */
export function isRealDay(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)

  if (!match) {
    return false
  }

  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])]
  const parsed = new Date(year, month - 1, day)

  return parsed.getFullYear() === year && parsed.getMonth() === month - 1 && parsed.getDate() === day
}

/**
 * A due date as a reader would write it — "5 Oct 2026".
 *
 * Built from the day's own parts rather than `new Date('2026-10-05')`, which JavaScript reads
 * as midnight UTC and which would show as the 4th anywhere west of Greenwich.
 */
export function formatDay(day: string): string {
  const [year, month, date] = day.split('-').map(Number)

  return new Date(year, month - 1, date).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

/**
 * Whether a milestone is late: it has a due date, that date is before `today`, and it is not
 * finished.
 *
 * A milestone with no date is never late — there is nothing to be late against — and a
 * completed one is not late however old its date, since it got done.
 */
export function isMilestoneOverdue(
  milestone: { dueDate: string | null; status: MilestoneStatus },
  today: string = todayLocal(),
): boolean {
  return milestone.dueDate !== null && milestone.status !== 'Completed' && milestone.dueDate < today
}
