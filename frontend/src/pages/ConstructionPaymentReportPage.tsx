import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { apiErrorMessage } from '@/lib/api'
import {
  CONSTRUCTION_PHASE_STATUS_LABELS,
  fetchConstructionProgressReport,
  type ConstructionProgressReportRow,
} from '@/lib/construction-api'
import { fetchPaymentReportSummary, type PaymentReportSummary } from '@/lib/payment-api'
import { fetchProjects } from '@/lib/project-api'

const CONSTRUCTION_LOAD_FAILED = 'Could not load the construction progress report.'
const PAYMENT_LOAD_FAILED = 'Could not load the payment summary.'

/** Which half of the report is on screen. */
type ReportView = 'construction' | 'payment'

/** Money as a reader would write it — grouped, two decimals, no currency symbol invented. */
function formatAmount(value: number): string {
  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

/**
 * A date input's value as the instant the API wants.
 *
 * The input gives a bare `YYYY-MM-DD` with no zone. Sent as-is the service would read it
 * as UTC midnight, which is what a reader picking "1 September" means by it — so the day
 * is anchored explicitly rather than run through the browser's own timezone, where a
 * Colombo user's "1 September" would become 31 August 18:30Z.
 */
function startOfDayUtc(date: string): string {
  return `${date}T00:00:00Z`
}

/**
 * The exclusive upper bound for a chosen end date: midnight at the *start of the next
 * day*, so the day the reader picked is included in full.
 *
 * Without this, picking 30 September would cut the range at that morning's first instant
 * and silently drop the whole day's invoices.
 */
function endOfDayExclusiveUtc(date: string): string {
  const next = new Date(`${date}T00:00:00Z`)
  next.setUTCDate(next.getUTCDate() + 1)

  return next.toISOString().replace(/\.\d{3}Z$/, 'Z')
}

/**
 * The combined construction and payment report (US-19): build progress across the active
 * projects, and the portfolio's invoiced, collected and outstanding totals.
 *
 * Admin and Project Manager only — the route guards it, and both endpoints behind it
 * answer 403 to anyone else.
 *
 * The two halves come from two services, each queried independently over its own schema:
 * Construction Service owns the milestones, Payment Service owns the invoices and
 * payments, and neither reads the other's database. Composing them is this page's job,
 * which is why each half carries its own loading and error state — one service being
 * unreachable leaves the other's figures on screen rather than blanking the report.
 */
export function ConstructionPaymentReportPage() {
  const { authFetch } = useAuth()

  const [view, setView] = useState<ReportView>('construction')

  const [rows, setRows] = useState<ConstructionProgressReportRow[] | null>(null)
  const [constructionError, setConstructionError] = useState<string | null>(null)
  /** Project id to name, for whichever projects this caller is allowed to see. */
  const [projectNames, setProjectNames] = useState<Map<string, string>>(new Map())

  const [summary, setSummary] = useState<PaymentReportSummary | null>(null)
  const [paymentError, setPaymentError] = useState<string | null>(null)

  // The range the reader has typed, not yet applied. Kept apart from what the loaded
  // summary covers so the figures on screen always describe the range they were fetched
  // with, rather than the one being edited.
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const [applying, setApplying] = useState(false)

  useEffect(() => {
    let cancelled = false

    fetchConstructionProgressReport(authFetch)
      .then((loaded) => {
        if (!cancelled) {
          setRows(loaded)
          setConstructionError(null)
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setConstructionError(apiErrorMessage(error, CONSTRUCTION_LOAD_FAILED))
        }
      })

    // Names, separately and best-effort. The report comes from the Construction
    // Service, which knows project ids and nothing else — a name belongs to the
    // Project Service. Its listing is scoped to what the caller may see: an Admin
    // gets every project, a Project Manager only the ones they are on. So a row
    // whose name cannot be resolved falls back to its short id rather than
    // disappearing, and a failure here never blocks the progress figures.
    fetchProjects(authFetch)
      .then((projects) => {
        if (!cancelled) {
          setProjectNames(new Map(projects.map((project) => [project.id, project.name])))
        }
      })
      .catch(() => {
        // Deliberately silent: the report is still complete and correct without
        // names, and an error banner here would suggest otherwise.
      })

    return () => {
      cancelled = true
    }
  }, [authFetch])

  /**
   * Reads the payment totals for the range currently entered.
   *
   * Both bounds are optional and independent — "everything since March" and "everything up
   * to March" are both reasonable asks. An empty field means no bound, which is how the
   * filter is cleared.
   */
  const loadPayments = useCallback(
    async (isCancelled: () => boolean) => {
      setApplying(true)

      try {
        const loaded = await fetchPaymentReportSummary(authFetch, {
          ...(fromDate ? { fromUtc: startOfDayUtc(fromDate) } : {}),
          ...(toDate ? { toUtc: endOfDayExclusiveUtc(toDate) } : {}),
        })

        if (!isCancelled()) {
          setSummary(loaded)
          setPaymentError(null)
        }
      } catch (error) {
        if (!isCancelled()) {
          // The service's own reason — an inverted range says so specifically, rather than
          // this page guessing at what went wrong.
          setPaymentError(apiErrorMessage(error, PAYMENT_LOAD_FAILED))
        }
      } finally {
        if (!isCancelled()) {
          setApplying(false)
        }
      }
    },
    [authFetch, fromDate, toDate],
  )

  // The unfiltered totals, once, on mount. The filter is applied by the button rather than
  // on every keystroke: a half-typed date would fire requests nobody asked for, and an
  // inverted intermediate range would flash an error the reader was about to fix.
  useEffect(() => {
    let cancelled = false
    void loadPayments(() => cancelled)

    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- mount only; the button re-runs it
  }, [authFetch])

  const filtered = summary !== null && (summary.fromUtc !== null || summary.toUtc !== null)

  return (
    <main className="mx-auto flex w-full max-w-7xl flex-col gap-6 px-4 py-8 sm:px-6 sm:py-10">
      <Card>
        <CardHeader>
          <CardTitle>Construction &amp; payment report</CardTitle>
          <CardDescription>
            Build progress across the active projects, and what has been invoiced, collected and
            is still owed across the portfolio.
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          {/* Two halves of one report, from two services. A segmented control rather than a
              route per half: the reader is comparing them, so switching should not reload. */}
          <div role="tablist" aria-label="Report view" className="flex gap-2">
            <Button
              type="button"
              role="tab"
              id="construction-tab"
              aria-selected={view === 'construction'}
              aria-controls="construction-panel"
              variant={view === 'construction' ? 'default' : 'outline'}
              size="sm"
              onClick={() => setView('construction')}
            >
              Construction
            </Button>
            <Button
              type="button"
              role="tab"
              id="payment-tab"
              aria-selected={view === 'payment'}
              aria-controls="payment-panel"
              variant={view === 'payment' ? 'default' : 'outline'}
              size="sm"
              onClick={() => setView('payment')}
            >
              Payments
            </Button>
          </div>

          {view === 'construction' ? (
            <section
              role="tabpanel"
              id="construction-panel"
              aria-labelledby="construction-tab"
              data-testid="construction-report"
              className="flex flex-col gap-3"
            >
              {constructionError && (
                <p role="alert" className="text-destructive text-sm">
                  {constructionError}
                </p>
              )}

              {!constructionError && rows === null && (
                <p className="text-muted-foreground text-sm">Loading construction progress…</p>
              )}

              {rows !== null && rows.length === 0 && (
                <p className="text-muted-foreground text-sm">
                  No active project has milestones defined yet. Progress appears here once a
                  project manager has planned a build.
                </p>
              )}

              {rows !== null && rows.length > 0 && (
                <>
                  {/* The report's own definition of "active", stated where the reader
                      is looking at the rows it selected. Taken from what the service
                      actually filters on, not restated by hand: a project appears once
                      its design is approved and milestones exist, and drops out only
                      when it is handed over. */}
                  <p className="text-muted-foreground text-sm">
                    <strong className="text-foreground font-medium">Active</strong> means the
                    project&rsquo;s design is approved and its milestones are planned, and the
                    build has not yet been handed over — so builds that are{' '}
                    <em>not started</em>, <em>started</em> and <em>completed</em> all appear
                    here. A handed-over project leaves the report; so does one with no
                    milestones planned yet.
                  </p>
                  <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Project</TableHead>
                      <TableHead>Phase</TableHead>
                      <TableHead className="text-right">Progress</TableHead>
                      <TableHead className="text-right">Done</TableHead>
                      <TableHead className="text-right">In progress</TableHead>
                      {/* "To do", not "Not started": the phase badge in the column beside
                          it already says "Not started" about the build itself, and two
                          columns using one phrase for different things reads as a mistake. */}
                      <TableHead className="text-right">To do</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {rows.map((row) => (
                      <TableRow key={row.projectId}>
                        <TableCell className="font-medium">
                          <Link
                            to={`/projects/${row.projectId}`}
                            className="underline underline-offset-4"
                            title={row.projectId}
                          >
                            {projectNames.get(row.projectId) ?? row.projectId.slice(0, 8)}
                          </Link>
                        </TableCell>
                        <TableCell>
                          {row.phaseStatus === null ? (
                            <Badge variant="outline">Not started</Badge>
                          ) : (
                            <Badge>{CONSTRUCTION_PHASE_STATUS_LABELS[row.phaseStatus]}</Badge>
                          )}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {row.progressPercent.toFixed(2)}%
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {row.completedMilestones} / {row.totalMilestones}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {row.inProgressMilestones}
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {row.notStartedMilestones}
                        </TableCell>
                      </TableRow>
                    ))}
                    </TableBody>
                  </Table>
                </>
              )}
            </section>
          ) : (
            <section
              role="tabpanel"
              id="payment-panel"
              aria-labelledby="payment-tab"
              data-testid="payment-report"
              className="flex flex-col gap-4"
            >
              <form
                className="flex flex-col gap-3 sm:flex-row sm:items-end"
                onSubmit={(event) => {
                  event.preventDefault()
                  void loadPayments(() => false)
                }}
              >
                <Field className="flex-1">
                  <FieldLabel htmlFor="fromDate">From</FieldLabel>
                  <Input
                    id="fromDate"
                    type="date"
                    value={fromDate}
                    onChange={(event) => setFromDate(event.target.value)}
                  />
                </Field>
                <Field className="flex-1">
                  <FieldLabel htmlFor="toDate">To</FieldLabel>
                  <Input
                    id="toDate"
                    type="date"
                    value={toDate}
                    onChange={(event) => setToDate(event.target.value)}
                  />
                </Field>
                <Button type="submit" size="sm" disabled={applying}>
                  {applying ? 'Applying…' : 'Apply'}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={applying || (fromDate === '' && toDate === '')}
                  onClick={() => {
                    setFromDate('')
                    setToDate('')
                  }}
                >
                  Clear
                </Button>
              </form>

              {paymentError && (
                <p role="alert" className="text-destructive text-sm">
                  {paymentError}
                </p>
              )}

              {!paymentError && summary === null && (
                <p className="text-muted-foreground text-sm">Loading payment totals…</p>
              )}

              {summary && (
                <>
                  <dl className="grid gap-4 sm:grid-cols-3">
                    <div className="flex flex-col gap-1">
                      <dt className="text-muted-foreground text-xs">Invoiced</dt>
                      <dd className="text-2xl font-semibold tabular-nums">
                        {formatAmount(summary.totalInvoiced)}
                      </dd>
                      <dd className="text-muted-foreground text-xs">
                        {summary.invoiceCount} invoice{summary.invoiceCount === 1 ? '' : 's'}
                      </dd>
                    </div>
                    <div className="flex flex-col gap-1">
                      <dt className="text-muted-foreground text-xs">Collected</dt>
                      <dd className="text-2xl font-semibold tabular-nums">
                        {formatAmount(summary.totalCollected)}
                      </dd>
                      <dd className="text-muted-foreground text-xs">
                        {summary.paymentCount} payment{summary.paymentCount === 1 ? '' : 's'}
                      </dd>
                    </div>
                    <div className="flex flex-col gap-1">
                      <dt className="text-muted-foreground text-xs">Outstanding</dt>
                      {/* Null under a filter, and shown as such. Invoiced is scoped by
                          invoice date and collected by payment date, so a figure here
                          inside a window could read as debt that does not exist. */}
                      <dd className="text-2xl font-semibold tabular-nums">
                        {summary.totalOutstanding === null
                          ? '—'
                          : formatAmount(summary.totalOutstanding)}
                      </dd>
                      <dd className="text-muted-foreground text-xs">
                        {summary.totalOutstanding === null
                          ? 'Only shown for the full ledger'
                          : 'Invoiced less collected'}
                      </dd>
                    </div>
                  </dl>

                  <p className="text-muted-foreground text-xs">
                    {filtered
                      ? 'Totals cover the selected dates. Outstanding is a whole-ledger figure, so it is not shown for a range.'
                      : 'Totals cover every invoice and payment on record.'}
                  </p>
                </>
              )}
            </section>
          )}

          <p className="text-muted-foreground text-center text-sm">
            <Link to="/home" className="text-foreground underline underline-offset-4">
              Back to home
            </Link>
          </p>
        </CardContent>
      </Card>
    </main>
  )
}
