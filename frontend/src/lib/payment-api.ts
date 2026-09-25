import { type ApiFetchOptions } from './api'

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
