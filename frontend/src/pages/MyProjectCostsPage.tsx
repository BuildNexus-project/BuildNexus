import { useCallback, useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'

import { useAuth } from '@/auth/auth-context'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { apiErrorMessage } from '@/lib/api'
import {
  fetchMyProjectInvoices,
  fetchMyProjectQuotations,
  paymentRefusal,
  recordPayment,
  type Invoice,
  type InvoiceStatus,
  type Quotation,
} from '@/lib/payment-api'
import { recordPaymentSchema, type RecordPaymentValues } from '@/lib/payment-schemas'
import { fetchProjects, type ProjectSummary } from '@/lib/project-api'
import { useAutoRefresh } from '@/lib/use-auto-refresh'

/** A `Paid` invoice reads as settled rather than as just another row. */
const INVOICE_BADGE_VARIANT: Record<InvoiceStatus, 'default' | 'secondary'> = {
  Pending: 'secondary',
  Paid: 'default',
}

/** Money as a reader would write it. The rest of the app prices in LKR. */
function formatMoney(amount: number): string {
  return amount.toLocaleString(undefined, {
    style: 'currency',
    currency: 'LKR',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
}

/** A timestamp the service sent, as a reader would write it. */
function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

/**
 * What this page knows about one project's cost.
 *
 * `error` carries anything that went wrong for that one project, so a single
 * failing project does not blank the whole page.
 */
type ProjectCostEntry = {
  project: ProjectSummary
  /** Newest first, so the first entry is the project's current estimate. */
  quotations: Quotation[]
  invoices: Invoice[]
  error: string | null
}

/**
 * The Client's cost view (US-15): what each of their projects was quoted, and
 * what has been billed against it.
 *
 * Client-only, matching the Payment Service's own gate — and the service
 * additionally refuses a project the caller does not own, so a Client cannot
 * reach another's costs by editing a URL.
 *
 * The data re-reads itself. That is not decoration here: an invoice is raised
 * automatically when a project's build starts, so a Client sitting on this page
 * can gain one without anybody typing it, and without a re-read they would not
 * see it until they navigated away and back.
 */
export function MyProjectCostsPage() {
  const { authFetch } = useAuth()

  const [entries, setEntries] = useState<ProjectCostEntry[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  /**
   * One full read: the Client's projects, then the costs behind each.
   *
   * Every project, with no status filter: a quotation can be raised long before
   * the build starts, and a project that is still Pending is exactly the one a
   * Client most wants a price for.
   */
  const load = useCallback(async () => {
    try {
      const projects = await fetchProjects(authFetch)

      // Concurrent, and each failure contained to its own card: one project the
      // service is unhappy about must not blank the rest of the page.
      const loaded = await Promise.all(
        projects.map(async (project): Promise<ProjectCostEntry> => {
          try {
            const [quotations, invoices] = await Promise.all([
              fetchMyProjectQuotations(authFetch, project.id),
              fetchMyProjectInvoices(authFetch, project.id),
            ])

            return { project, quotations, invoices, error: null }
          } catch (error) {
            return {
              project,
              quotations: [],
              invoices: [],
              error: apiErrorMessage(error, 'Could not load the costs for this project.'),
            }
          }
        }),
      )

      setEntries(loaded)
      setLoadError(null)
    } catch (error) {
      // The projects call itself failed, so there is nothing to show.
      setLoadError(apiErrorMessage(error, 'Could not load your projects.'))
    }
  }, [authFetch])

  // The initial read. useAutoRefresh deliberately does not fire on mount — it
  // polls and reacts to the tab becoming visible — so the first load is the
  // caller's, and doing both there would fetch twice on every visit.
  useEffect(() => {
    void load()
  }, [load])

  useAutoRefresh(useCallback(() => void load(), [load]))

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-3xl flex-col gap-4 p-6">
      <Card>
        <CardHeader>
          <CardTitle>Your project costs</CardTitle>
          <CardDescription>
            What each of your projects is expected to cost, and what has been billed so far.
          </CardDescription>
        </CardHeader>

        {loadError ? (
          <CardContent className="text-destructive text-sm">{loadError}</CardContent>
        ) : null}
      </Card>

      {entries === null && !loadError ? (
        <p className="text-muted-foreground text-sm">Loading your costs…</p>
      ) : null}

      {entries?.length === 0 ? (
        <Card>
          <CardContent className="text-muted-foreground pt-6 text-sm">
            You have no projects yet.
          </CardContent>
        </Card>
      ) : null}

      {entries?.map((entry) => (
        <ProjectCostCard key={entry.project.id} entry={entry} onPaid={load} />
      ))}
    </main>
  )
}

/** One project: its current estimate, and every invoice raised against it. */
function ProjectCostCard({
  entry,
  onPaid,
}: {
  entry: ProjectCostEntry
  onPaid: () => Promise<void>
}) {
  const { project, quotations, invoices, error } = entry

  // The service returns quotations newest first, so the front of the list is
  // the figure that counts.
  const currentQuotation = quotations.at(0) ?? null
  const invoicedTotal = invoices.reduce((total, invoice) => total + invoice.amount, 0)

  return (
    <Card>
      <CardHeader>
        <CardTitle>{project.name}</CardTitle>
        <CardDescription>{project.location}</CardDescription>
      </CardHeader>

      <CardContent className="flex flex-col gap-4">
        {error ? <p className="text-destructive text-sm">{error}</p> : null}

        {!error && (
          <>
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <span className="text-muted-foreground text-xs">Estimated cost</span>
              {currentQuotation ? (
                <span className="text-2xl font-semibold tabular-nums">
                  {formatMoney(currentQuotation.estimatedTotal)}
                </span>
              ) : (
                <span className="text-muted-foreground text-sm">
                  Not quoted yet — your project manager will send an estimate.
                </span>
              )}
            </div>

            {currentQuotation ? (
              <p className="text-muted-foreground text-xs">
                Quoted {formatDate(currentQuotation.createdAtUtc)}
                {/* Re-quoting keeps the earlier figures, so say when the price
                    has moved rather than silently showing a different number
                    than the Client remembers. */}
                {quotations.length > 1
                  ? ` · revised ${quotations.length - 1} time${quotations.length === 2 ? '' : 's'}`
                  : null}
              </p>
            ) : null}

            <div className="flex flex-col gap-2 border-t pt-4">
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <span className="text-muted-foreground text-xs">Billed so far</span>
                <span className="font-semibold tabular-nums">{formatMoney(invoicedTotal)}</span>
              </div>

              {invoices.length === 0 ? (
                <p className="text-muted-foreground text-sm">
                  Nothing has been billed for this project yet.
                </p>
              ) : (
                <ul className="flex flex-col gap-3">
                  {invoices.map((invoice) => (
                    <li key={invoice.id} className="border-b pb-3 last:border-b-0 last:pb-0">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <span className="text-sm font-medium tabular-nums">
                          {formatMoney(invoice.amount)}
                        </span>
                        <span className="flex items-center gap-2">
                          <span className="text-muted-foreground text-xs">
                            {formatDate(invoice.createdAtUtc)}
                          </span>
                          <Badge variant={INVOICE_BADGE_VARIANT[invoice.status]}>
                            {invoice.status}
                          </Badge>
                        </span>
                      </div>

                      {/* Only a Pending invoice can be paid. A settled one keeps
                          its badge and loses the form — there is nothing left to
                          pay, and the service would refuse it anyway. */}
                      {invoice.status === 'Pending' ? (
                        <PayInvoiceForm invoice={invoice} onPaid={onPaid} />
                      ) : null}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  )
}

/**
 * The Client's pay control for one pending invoice (US-16).
 *
 * The service is the authority on what may be paid: the outstanding amount can
 * change between this rendering and the request landing, so the form does not
 * try to cap the figure itself. When the service refuses an over-payment it
 * sends back the invoice's real outstanding amount, and that is shown here as a
 * correction rather than a bare error.
 */
function PayInvoiceForm({ invoice, onPaid }: { invoice: Invoice; onPaid: () => Promise<void> }) {
  const { authFetch } = useAuth()
  const [formError, setFormError] = useState<string | null>(null)
  /** What the service says is actually left, after it refused an over-payment. */
  const [payableAmount, setPayableAmount] = useState<number | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<RecordPaymentValues>({
    resolver: zodResolver(recordPaymentSchema),
  })

  async function onSubmit(values: RecordPaymentValues) {
    setFormError(null)
    setPayableAmount(null)

    try {
      await recordPayment(authFetch, invoice.id, { amount: values.amount })
      reset()
      // Re-read rather than patching the row in place: the payment may have
      // settled the invoice, and the card shows a billed total and a status that
      // both move with it.
      await onPaid()
    } catch (error) {
      const refusal = paymentRefusal(error)

      // An over-payment is the one failure the screen can help with — it knows
      // the figure that would have worked, so it offers it.
      if (refusal?.reason === 'ExceedsOutstanding' && refusal.outstandingAmount !== null) {
        setPayableAmount(refusal.outstandingAmount)
      }

      setFormError(apiErrorMessage(error, 'Could not record your payment. Please try again.'))
    }
  }

  return (
    <form
      onSubmit={handleSubmit(onSubmit)}
      noValidate
      className="mt-3 flex flex-col gap-2"
      aria-label={`Pay invoice of ${formatMoney(invoice.amount)}`}
    >
      <div className="flex flex-wrap items-end gap-2">
        <Field className="flex-1">
          <FieldLabel htmlFor={`amount-${invoice.id}`}>Payment amount (LKR)</FieldLabel>
          <Input
            id={`amount-${invoice.id}`}
            type="number"
            inputMode="decimal"
            step="0.01"
            min="0.01"
            aria-invalid={Boolean(errors.amount)}
            {...register('amount', { valueAsNumber: true })}
          />
        </Field>

        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Recording…' : 'Pay'}
        </Button>
      </div>

      <FieldError errors={[errors.amount]} />

      {formError ? <p className="text-destructive text-sm">{formError}</p> : null}

      {payableAmount !== null ? (
        <p className="text-sm">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => {
              setValue('amount', payableAmount, { shouldValidate: true })
              setFormError(null)
              setPayableAmount(null)
            }}
          >
            Pay {formatMoney(payableAmount)} instead
          </Button>
        </p>
      ) : null}
    </form>
  )
}
