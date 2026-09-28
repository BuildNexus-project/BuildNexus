import { ApiError, type ApiFetchOptions } from './api'

/** The token-attaching fetch handed out by the auth context. */
type AuthFetch = <T>(path: string, options?: Omit<ApiFetchOptions, 'token'>) => Promise<T>

/**
 * The two states an invoice can be in, exactly as the Payment Service spells
 * them (US-15 AC-2). The wire format is the string name — a global
 * `JsonStringEnumConverter` on the service side keeps request and response in
 * the same shape, and refuses the integer form outright.
 */
export const INVOICE_STATUSES = ['Pending', 'Paid'] as const

export type InvoiceStatus = (typeof INVOICE_STATUSES)[number]

/**
 * One cost estimate raised against a project (US-15 AC-1).
 *
 * A project may have several: re-quoting as scope firms up does not overwrite
 * the earlier figure, so the list is a history and its first entry — the
 * service returns them newest first — is the project's current estimate.
 */
export type Quotation = {
  id: string
  projectId: string
  /**
   * The estimated total. A `number` on the wire, from a `DECIMAL(15,2)` column —
   * exact to the cent for every figure a construction project carries, well
   * inside the range where a double represents two decimal places faithfully.
   */
  estimatedTotal: number
  /** The Project Manager or Admin who generated it. */
  createdBy: string
  /** ISO-8601, as the service serialises it. */
  createdAtUtc: string
}

/**
 * One invoice raised against a project (US-15 AC-2).
 *
 * Raised either by a Project Manager or Admin by hand, or automatically when
 * the project's build starts — both produce the same shape, so a screen does
 * not have to care which happened.
 */
export type Invoice = {
  /** AC-2's unique ID. */
  id: string
  projectId: string
  amount: number
  status: InvoiceStatus
  /**
   * Who raised it: the staff member who asked for it, or — for one raised
   * automatically — the Project Manager whose decision to start the build
   * triggered it.
   */
  createdBy: string
  createdAtUtc: string
  /** When it was settled, or `null` while it is still `Pending`. */
  paidAtUtc: string | null
}

/** What {@link generateQuotation} sends. The project id travels in the URL. */
export type GenerateQuotationPayload = {
  estimatedTotal: number
}

/** What {@link generateInvoice} sends. */
export type GenerateInvoicePayload = {
  amount: number
}

/**
 * Generates a cost quotation for a project (US-15 AC-1).
 *
 * Project-Manager or Admin only; any other role gets 403. Always creates a new
 * quotation rather than replacing the project's existing one — a project can be
 * re-quoted, and the earlier figures are the record of what the Client was told
 * before. A zero, negative or over-large total comes back as 400.
 */
export function generateQuotation(
  authFetch: AuthFetch,
  projectId: string,
  payload: GenerateQuotationPayload,
) {
  return authFetch<Quotation>(`/api/payments/projects/${projectId}/quotations`, {
    method: 'POST',
    json: payload,
  })
}

/**
 * A project's quotations, newest first — so the first entry is its current
 * estimate and the rest are its history.
 *
 * Project-Manager or Admin only. Empty is a real answer for a project that has
 * never been quoted, not a failure.
 */
export function fetchProjectQuotations(authFetch: AuthFetch, projectId: string) {
  return authFetch<Quotation[]>(`/api/payments/projects/${projectId}/quotations`)
}

/**
 * Raises an invoice against a project (US-15 AC-2).
 *
 * Project-Manager or Admin only. The invoice is always created `Pending` — the
 * request carries no status, so nothing can declare one already settled.
 *
 * This is the manual path. An invoice is also raised automatically when the
 * project's build starts, off the Construction Service's `ConstructionStarted`
 * event, so a project's invoice list may gain an entry nobody typed.
 */
export function generateInvoice(
  authFetch: AuthFetch,
  projectId: string,
  payload: GenerateInvoicePayload,
) {
  return authFetch<Invoice>(`/api/payments/projects/${projectId}/invoices`, {
    method: 'POST',
    json: payload,
  })
}

/**
 * A project's invoices, newest first.
 *
 * Project-Manager or Admin only. Empty is a real answer for a project that has
 * not been billed yet.
 */
export function fetchProjectInvoices(authFetch: AuthFetch, projectId: string) {
  return authFetch<Invoice[]>(`/api/payments/projects/${projectId}/invoices`)
}

/**
 * The quotations for one of the signed-in Client's own projects (US-15 AC-1 —
 * "viewable by the Client").
 *
 * Client only, and only for a project they own: the service answers 403 for
 * anyone else's project, and the same 403 whether the project is unquoted or
 * does not exist — so nothing here can be used to probe for project ids.
 */
export function fetchMyProjectQuotations(authFetch: AuthFetch, projectId: string) {
  return authFetch<Quotation[]>(`/api/payments/my-projects/${projectId}/quotations`)
}

/**
 * The invoices raised against one of the signed-in Client's own projects.
 *
 * Client only and ownership-scoped, exactly as
 * {@link fetchMyProjectQuotations} is.
 */
export function fetchMyProjectInvoices(authFetch: AuthFetch, projectId: string) {
  return authFetch<Invoice[]>(`/api/payments/my-projects/${projectId}/invoices`)
}

/**
 * One payment a Client recorded against an invoice (US-16).
 */
export type Payment = {
  id: string
  invoiceId: string
  amount: number
  /** The Client who recorded it. */
  paidBy: string
  /** ISO-8601, as the service serialises it. */
  recordedAtUtc: string
}

/**
 * What recording a payment answers with: the payment, and what it left behind.
 *
 * The invoice's new state travels back with the payment rather than leaving the
 * screen to re-read it — a Client who has just paid wants to know how much is
 * still owed and whether the invoice is settled, and a second round trip would
 * show a stale balance in between.
 */
export type RecordPaymentResult = {
  payment: Payment
  /** What is still owed after this payment. Zero once the invoice is settled. */
  outstandingAmount: number
  /** `Paid` here means this payment settled the invoice. */
  invoiceStatus: InvoiceStatus
}

/** What {@link recordPayment} sends. The invoice travels in the URL. */
export type RecordPaymentPayload = {
  amount: number
}

/**
 * Why the service refused a payment, when it did.
 *
 * Read off the problem details' `reason` extension rather than its prose, so the
 * screen keeps working if the wording is improved.
 */
export type PaymentRefusalReason = 'ExceedsOutstanding' | 'AlreadyPaid'

/**
 * The refusal as something a screen can act on, or `null` for any other failure.
 *
 * `outstandingAmount` is what the invoice actually has left — the service sends
 * it precisely so an over-payment can offer the figure that would have worked
 * rather than only reporting the error.
 */
export function paymentRefusal(
  error: unknown,
): { reason: PaymentRefusalReason; outstandingAmount: number | null } | null {
  if (!(error instanceof ApiError) || error.status !== 409) {
    return null
  }

  if (error.reason !== 'ExceedsOutstanding' && error.reason !== 'AlreadyPaid') {
    return null
  }

  const outstanding = error.extensions.outstandingAmount

  return {
    reason: error.reason,
    outstandingAmount: typeof outstanding === 'number' ? outstanding : null,
  }
}

/**
 * Records a payment against one of the Client's own invoices (US-16).
 *
 * Client only, and only for an invoice on a project they own: the service
 * answers 403 for anyone else's, and the same 403 whether the invoice exists or
 * not — so nothing here can be used to probe for invoice ids.
 *
 * A payment larger than the invoice's outstanding amount is refused with 409 and
 * a `reason` of `ExceedsOutstanding`; an invoice that is already settled is
 * refused with `AlreadyPaid`. {@link paymentRefusal} turns either into something
 * the screen can branch on.
 */
export function recordPayment(
  authFetch: AuthFetch,
  invoiceId: string,
  payload: RecordPaymentPayload,
) {
  return authFetch<RecordPaymentResult>(`/api/payments/invoices/${invoiceId}/payments`, {
    method: 'POST',
    json: payload,
  })
}

/**
 * One invoice with everything paid against it (US-17).
 */
export type InvoiceWithPayments = {
  invoice: Invoice
  /** Oldest first — the order the Client made them. */
  payments: Payment[]
  /** What has been paid against this invoice so far. */
  amountPaid: number
  /**
   * What is still owed on this invoice. Zero once settled, and exactly the
   * figure {@link recordPayment} will accept — the service computes both with
   * the same arithmetic, so a Client is never offered an amount it then refuses.
   */
  outstandingAmount: number
}

/**
 * A project's billing picture in one answer: the history and the balance
 * (US-17).
 *
 * Both halves arrive together because AC-2 requires the balance on the same
 * view as the history — two requests would let a payment land between them and
 * show a balance that disagrees with the invoices beneath it.
 */
export type ProjectPaymentHistory = {
  projectId: string
  /** Most recent first. */
  invoices: InvoiceWithPayments[]
  /** Everything ever billed on this project, settled or not. */
  totalInvoiced: number
  /** Everything ever paid on this project. */
  totalPaid: number
  /** What the Client still owes across the project. */
  outstandingBalance: number
}

/**
 * The full billing history for one of the Client's own projects, with the
 * balance still outstanding (US-17).
 *
 * Client only and ownership-scoped, exactly as the other `my-projects` reads
 * are: the service answers 403 for anyone else's project, and the same 403
 * whether it exists or not.
 *
 * A project with no invoices is a real answer — an empty list at a zero balance
 * — not a failure.
 */
export function fetchMyProjectPaymentHistory(authFetch: AuthFetch, projectId: string) {
  return authFetch<ProjectPaymentHistory>(`/api/payments/my-projects/${projectId}/history`)
}

/**
 * The portfolio's financial totals (US-19 AC-2).
 *
 * `totalOutstanding` is `null` whenever a date range was applied. Invoiced is scoped by
 * when an invoice was raised and collected by when a payment was recorded, so inside a
 * window their difference can be negative and means nothing — "what is still owed" is a
 * question about the whole ledger. The page shows the figure only when it is meaningful
 * rather than printing a number that reads as debt.
 *
 * The two counts let a reader tell one large invoice from fifty small ones, and tell a
 * genuine zero from an empty result.
 */
export type PaymentReportSummary = {
  totalInvoiced: number
  totalCollected: number
  totalOutstanding: number | null
  invoiceCount: number
  paymentCount: number
  /** ISO-8601, echoed back from the request; `null` when unfiltered. */
  fromUtc: string | null
  toUtc: string | null
}

/** An optional window for {@link fetchPaymentReportSummary}. Both bounds are independent. */
export type PaymentReportRange = {
  /** Inclusive lower bound, ISO-8601. */
  fromUtc?: string
  /** Exclusive upper bound, ISO-8601 — so two adjacent ranges neither overlap nor skip a row. */
  toUtc?: string
}

/**
 * Invoiced, collected and outstanding totals across every project, optionally narrowed to
 * a date range (US-19 AC-2).
 *
 * Project Manager and Admin only; the service answers 403 to anyone else. An inverted or
 * empty range comes back as {@link ApiError} with status 400 rather than as zeros, which
 * would read as "nothing was billed".
 */
export function fetchPaymentReportSummary(authFetch: AuthFetch, range: PaymentReportRange = {}) {
  const query = new URLSearchParams()

  if (range.fromUtc) {
    query.set('fromUtc', range.fromUtc)
  }

  if (range.toUtc) {
    query.set('toUtc', range.toUtc)
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : ''

  return authFetch<PaymentReportSummary>(`/api/payments/reports/summary${suffix}`)
}
