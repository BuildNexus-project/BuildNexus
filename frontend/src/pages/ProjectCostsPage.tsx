import { useCallback, useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useParams } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
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
import { applyApiErrorToForm } from '@/lib/form-errors'
import {
  fetchProjectInvoices,
  fetchProjectQuotations,
  generateInvoice,
  generateQuotation,
  type Invoice,
  type InvoiceStatus,
  type Quotation,
} from '@/lib/payment-api'
import {
  generateInvoiceSchema,
  generateQuotationSchema,
  type GenerateInvoiceValues,
  type GenerateQuotationValues,
} from '@/lib/payment-schemas'

/** A `Paid` invoice reads as settled rather than as just another row. */
const INVOICE_BADGE_VARIANT: Record<InvoiceStatus, 'default' | 'secondary'> = {
  Pending: 'secondary',
  Paid: 'default',
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

/** A timestamp the service sent, as a reader would write it. */
export function formatTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

/**
 * Generate a project's quotation and its invoices (US-15).
 *
 * Project-Manager and Admin only — the route guard keeps other roles out, and
 * both endpoints behind it refuse them with 403 whatever the browser does.
 *
 * The two halves sit on one screen deliberately: the story's purpose is that
 * the expected cost and the current cost are both knowable, and raising an
 * invoice without the estimate in front of you is how the two drift apart.
 */
export function ProjectCostsPage() {
  const { authFetch } = useAuth()
  const { projectId } = useParams<{ projectId: string }>()

  const [quotations, setQuotations] = useState<Quotation[]>([])
  const [invoices, setInvoices] = useState<Invoice[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  const load = useCallback(async () => {
    if (!projectId) {
      return
    }

    setLoadError(null)

    try {
      // Both in flight at once: neither read depends on the other, and doing
      // them in sequence would show the invoices a round trip after the
      // estimate they are measured against.
      const [nextQuotations, nextInvoices] = await Promise.all([
        fetchProjectQuotations(authFetch, projectId),
        fetchProjectInvoices(authFetch, projectId),
      ])

      setQuotations(nextQuotations)
      setInvoices(nextInvoices)
    } catch (error) {
      setLoadError(apiErrorMessage(error, "Could not load this project's costs."))
    } finally {
      setIsLoading(false)
    }
  }, [authFetch, projectId])

  useEffect(() => {
    void load()
  }, [load])

  // The project's current estimate. The service returns quotations newest
  // first, so this is the front of the list rather than a second request.
  const currentQuotation = quotations.at(0) ?? null
  const invoicedTotal = invoices.reduce((total, invoice) => total + invoice.amount, 0)

  if (!projectId) {
    return null
  }

  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-6 p-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold">Project costs</h1>
        <p className="text-muted-foreground text-sm">
          The estimate this project was quoted, and what has been billed against it.{' '}
          <Link to={`/projects/${projectId}`} className="underline underline-offset-4">
            Back to the project
          </Link>
        </p>
      </div>

      {loadError ? (
        <Card>
          <CardContent className="text-destructive pt-6 text-sm">{loadError}</CardContent>
        </Card>
      ) : null}

      <div className="grid gap-6 lg:grid-cols-2">
        <QuotationPanel
          projectId={projectId}
          quotations={quotations}
          currentQuotation={currentQuotation}
          isLoading={isLoading}
          onGenerated={load}
        />

        <InvoicePanel
          projectId={projectId}
          invoices={invoices}
          invoicedTotal={invoicedTotal}
          currentQuotation={currentQuotation}
          isLoading={isLoading}
          onGenerated={load}
        />
      </div>
    </main>
  )
}

/** The estimate half: generate one, and see the history of what came before. */
function QuotationPanel({
  projectId,
  quotations,
  currentQuotation,
  isLoading,
  onGenerated,
}: {
  projectId: string
  quotations: Quotation[]
  currentQuotation: Quotation | null
  isLoading: boolean
  onGenerated: () => Promise<void>
}) {
  const { authFetch } = useAuth()
  const [formError, setFormError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<GenerateQuotationValues>({
    resolver: zodResolver(generateQuotationSchema),
  })

  async function onSubmit(values: GenerateQuotationValues) {
    setFormError(null)

    try {
      await generateQuotation(authFetch, projectId, { estimatedTotal: values.estimatedTotal })
      reset()
      await onGenerated()
    } catch (error) {
      setFormError(
        applyApiErrorToForm(error, setError, 'Could not generate the quotation. Please try again.'),
      )
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Quotation</CardTitle>
        <CardDescription>
          {currentQuotation
            ? `Currently estimated at ${formatMoney(currentQuotation.estimatedTotal)}.`
            : 'This project has not been quoted yet.'}
        </CardDescription>
      </CardHeader>

      <CardContent className="flex flex-col gap-6">
        <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
          <Field>
            <FieldLabel htmlFor="estimatedTotal">Estimated total (LKR)</FieldLabel>
            <Input
              id="estimatedTotal"
              type="number"
              inputMode="decimal"
              step="0.01"
              min="0.01"
              aria-invalid={Boolean(errors.estimatedTotal)}
              {...register('estimatedTotal', { valueAsNumber: true })}
            />
            <FieldDescription>
              {currentQuotation
                ? 'Re-quoting keeps the earlier figure as history rather than replacing it.'
                : 'What the build is expected to cost.'}
            </FieldDescription>
            <FieldError errors={[errors.estimatedTotal]} />
          </Field>

          {formError ? <p className="text-destructive text-sm">{formError}</p> : null}

          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? 'Generating…' : 'Generate quotation'}
          </Button>
        </form>

        <QuotationHistory quotations={quotations} isLoading={isLoading} />
      </CardContent>
    </Card>
  )
}

function QuotationHistory({
  quotations,
  isLoading,
}: {
  quotations: Quotation[]
  isLoading: boolean
}) {
  if (isLoading) {
    return <p className="text-muted-foreground text-sm">Loading quotations…</p>
  }

  if (quotations.length === 0) {
    return <p className="text-muted-foreground text-sm">No quotations yet.</p>
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Estimated total</TableHead>
          <TableHead>Generated</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {quotations.map((quotation, index) => (
          <TableRow key={quotation.id}>
            <TableCell className="font-medium">
              {formatMoney(quotation.estimatedTotal)}{' '}
              {/* The service returns these newest first, so the first row is
                  the figure that counts and the rest are what it replaced. */}
              {index === 0 ? <Badge variant="secondary">Current</Badge> : null}
            </TableCell>
            <TableCell>{formatTime(quotation.createdAtUtc)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}

/** The billing half: raise an invoice, and see everything billed so far. */
function InvoicePanel({
  projectId,
  invoices,
  invoicedTotal,
  currentQuotation,
  isLoading,
  onGenerated,
}: {
  projectId: string
  invoices: Invoice[]
  invoicedTotal: number
  currentQuotation: Quotation | null
  isLoading: boolean
  onGenerated: () => Promise<void>
}) {
  const { authFetch } = useAuth()
  const [formError, setFormError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<GenerateInvoiceValues>({
    resolver: zodResolver(generateInvoiceSchema),
  })

  async function onSubmit(values: GenerateInvoiceValues) {
    setFormError(null)

    try {
      await generateInvoice(authFetch, projectId, { amount: values.amount })
      reset()
      await onGenerated()
    } catch (error) {
      setFormError(
        applyApiErrorToForm(error, setError, 'Could not raise the invoice. Please try again.'),
      )
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Invoices</CardTitle>
        <CardDescription>
          {invoices.length === 0
            ? 'Nothing has been billed against this project yet.'
            : `${formatMoney(invoicedTotal)} billed across ${invoices.length} invoice${
                invoices.length === 1 ? '' : 's'
              }${currentQuotation ? ` of ${formatMoney(currentQuotation.estimatedTotal)} estimated` : ''}.`}
        </CardDescription>
      </CardHeader>

      <CardContent className="flex flex-col gap-6">
        <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-4">
          <Field>
            <FieldLabel htmlFor="amount">Amount (LKR)</FieldLabel>
            <Input
              id="amount"
              type="number"
              inputMode="decimal"
              step="0.01"
              min="0.01"
              aria-invalid={Boolean(errors.amount)}
              {...register('amount', { valueAsNumber: true })}
            />
            <FieldDescription>
              Raised as Pending. An invoice is also raised automatically when this project&apos;s
              build starts.
            </FieldDescription>
            <FieldError errors={[errors.amount]} />
          </Field>

          {formError ? <p className="text-destructive text-sm">{formError}</p> : null}

          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? 'Raising…' : 'Raise invoice'}
          </Button>
        </form>

        <InvoiceList invoices={invoices} isLoading={isLoading} />
      </CardContent>
    </Card>
  )
}

function InvoiceList({ invoices, isLoading }: { invoices: Invoice[]; isLoading: boolean }) {
  if (isLoading) {
    return <p className="text-muted-foreground text-sm">Loading invoices…</p>
  }

  if (invoices.length === 0) {
    return <p className="text-muted-foreground text-sm">No invoices yet.</p>
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Amount</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Raised</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {invoices.map((invoice) => (
          <TableRow key={invoice.id}>
            <TableCell className="font-medium">{formatMoney(invoice.amount)}</TableCell>
            <TableCell>
              <Badge variant={INVOICE_BADGE_VARIANT[invoice.status]}>{invoice.status}</Badge>
            </TableCell>
            <TableCell>{formatTime(invoice.createdAtUtc)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}
