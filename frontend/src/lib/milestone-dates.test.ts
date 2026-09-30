import { describe, expect, it } from 'vitest'

import { formatDay, isMilestoneOverdue, isRealDay, todayLocal } from './milestone-dates'

/**
 * A milestone's due date is a calendar day, handled as a `yyyy-MM-dd` string so no timezone
 * comes with it. These pin the four things done with one: saying what today is, telling a
 * real day from a well-formed impossible one, writing it for a reader, and deciding whether
 * a milestone is late.
 */

describe('todayLocal', () => {
  it('is the reader’s own calendar day as yyyy-MM-dd', () => {
    expect(todayLocal(new Date(2026, 9, 5, 14, 30))).toBe('2026-10-05')
  })

  it('pads a single-digit month and day', () => {
    expect(todayLocal(new Date(2026, 0, 3))).toBe('2026-01-03')
  })

  it('is still the same day at a minute to midnight', () => {
    // Local, not UTC: a Project Manager late in the evening in Colombo is still on their own
    // 5th, even though it is already the 6th in UTC.
    expect(todayLocal(new Date(2026, 9, 5, 23, 59))).toBe('2026-10-05')
  })
})

describe('isRealDay', () => {
  it.each(['2026-10-05', '2026-02-28', '2028-02-29', '2026-12-31'])('accepts %s', (day) => {
    expect(isRealDay(day)).toBe(true)
  })

  it.each([
    ['2026-02-30', 'a day February does not have'],
    ['2026-02-29', 'a leap day in a year that is not one'],
    ['2026-13-01', 'a thirteenth month'],
    ['2026-00-10', 'month zero'],
    ['2026-10-32', 'a thirty-second day'],
  ])('refuses %s — %s', (day) => {
    expect(isRealDay(day)).toBe(false)
  })

  it.each(['', '5 Oct 2026', '2026/10/05', '26-10-05', '2026-1-5', 'not a date'])(
    'refuses %j, which is not yyyy-MM-dd',
    (value) => {
      expect(isRealDay(value)).toBe(false)
    },
  )
})

describe('formatDay', () => {
  it('writes the day, month and year', () => {
    const written = formatDay('2026-10-05')

    expect(written).toMatch(/2026/)
    expect(written).toMatch(/5/)
  })

  it('shows the day it was given, not the day before', () => {
    // `new Date('2026-10-05')` is midnight UTC, which reads as the 4th anywhere west of
    // Greenwich. The day is built from its own parts, so it cannot drift.
    expect(formatDay('2026-10-05')).toBe(
      new Date(2026, 9, 5).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' }),
    )
  })

  it('gives the first and last day of a month their own day', () => {
    expect(formatDay('2026-01-31')).toMatch(/31/)
    expect(formatDay('2026-02-01')).toMatch(/1/)
  })
})

describe('isMilestoneOverdue', () => {
  const today = '2026-10-05'

  it('is late when it is not finished and its due date is before today', () => {
    expect(isMilestoneOverdue({ dueDate: '2026-10-04', status: 'InProgress' }, today)).toBe(true)
    expect(isMilestoneOverdue({ dueDate: '2026-01-01', status: 'NotStarted' }, today)).toBe(true)
  })

  it('is not late on the day it is due — it still has today', () => {
    expect(isMilestoneOverdue({ dueDate: '2026-10-05', status: 'InProgress' }, today)).toBe(false)
  })

  it('is not late when it is due later', () => {
    expect(isMilestoneOverdue({ dueDate: '2026-10-06', status: 'NotStarted' }, today)).toBe(false)
  })

  it('is never late with no due date — there is nothing to be late against', () => {
    expect(isMilestoneOverdue({ dueDate: null, status: 'NotStarted' }, today)).toBe(false)
  })

  it('is not late once completed, however old its due date', () => {
    expect(isMilestoneOverdue({ dueDate: '2020-01-01', status: 'Completed' }, today)).toBe(false)
  })

  it('judges against the real today when none is given', () => {
    expect(isMilestoneOverdue({ dueDate: '2000-01-01', status: 'InProgress' })).toBe(true)
    expect(isMilestoneOverdue({ dueDate: '2999-01-01', status: 'InProgress' })).toBe(false)
  })
})
