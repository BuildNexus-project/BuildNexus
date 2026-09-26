/**
 * An unsuccessful API response, carrying whatever detail the service gave us.
 *
 * The User Service answers with RFC 7807 problem details: a `title` and
 * `detail` for ordinary failures, plus a per-field `errors` map for validation
 * failures. Both are preserved so a form can show the specific message rather
 * than "something went wrong".
 */
export class ApiError extends Error {
  readonly status: number
  readonly title?: string
  readonly detail?: string
  readonly fieldErrors: Record<string, string[]>
  /**
   * A machine-readable reason, from the problem details' `reason` extension,
   * when the service sent one.
   *
   * Most refusals need nothing but `detail` on screen. This is for the few where
   * the caller has to *act* differently depending on which precondition failed —
   * the Construction Service's build transitions (US-14), where an "already
   * started" means the screen is stale and should re-read, while a "no milestones
   * defined" means the user has something to do. Branching on `detail` would mean
   * matching on English prose, which breaks the moment the wording is improved.
   */
  readonly reason?: string

  /**
   * Every other field the problem details carried, verbatim.
   *
   * RFC 7807 lets a service add its own members, and some refusals are only
   * actionable with one — US-16's over-payment sends back the invoice's real
   * `outstandingAmount`, so a screen can offer the figure that would have worked
   * rather than only reporting the error. Kept as a bag rather than a growing
   * list of named fields, so a service adding an extension does not mean editing
   * this class again. Values are `unknown`: a caller reads one by narrowing it,
   * the same way {@link reason} is only carried when it is a string.
   */
  readonly extensions: Record<string, unknown>

  constructor(
    status: number,
    options: {
      title?: string
      detail?: string
      fieldErrors?: Record<string, string[]>
      reason?: string
      extensions?: Record<string, unknown>
    } = {},
  ) {
    super(options.detail ?? options.title ?? `Request failed with status ${status}.`)
    this.name = 'ApiError'
    this.status = status
    this.title = options.title
    this.detail = options.detail
    this.fieldErrors = options.fieldErrors ?? {}
    this.reason = options.reason
    this.extensions = options.extensions ?? {}
  }
}

/**
 * The message to show for a failed request that is not a form submission.
 *
 * A refusal from the service is a real answer with a reason in it — "you are
 * not on this project", "a Designing project cannot move to Completed" — and
 * showing that reason beats flattening every failure into "something went
 * wrong". Anything that is not an {@link ApiError} (a dropped connection,
 * unparseable JSON) has no reason to show, so it gets the caller's fallback.
 */
export function apiErrorMessage(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError)) {
    return fallback
  }

  return error.detail ?? error.title ?? fallback
}

type ProblemDetails = {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
  /** An optional extension some services add to name the precondition that refused. */
  reason?: unknown
}

/** The members RFC 7807 defines, which are not extensions. */
const STANDARD_PROBLEM_MEMBERS = ['type', 'title', 'status', 'detail', 'instance', 'errors']

/** Whatever the service added beyond the standard members. */
function problemExtensions(problem: ProblemDetails): Record<string, unknown> {
  return Object.fromEntries(
    Object.entries(problem).filter(([key]) => !STANDARD_PROBLEM_MEMBERS.includes(key)),
  )
}

export type ApiFetchOptions = RequestInit & {
  /** Access token to send as `Authorization: Bearer`. */
  token?: string | null
  /** Body to serialise as JSON. */
  json?: unknown
}

/**
 * Calls the API and returns the parsed body, throwing {@link ApiError} on any
 * non-2xx response.
 *
 * Paths are relative (`/api/auth/login`); the dev server proxies them to the
 * User Service, so calls are same-origin and no CORS handling is needed.
 */
export async function apiFetch<T>(path: string, options: ApiFetchOptions = {}): Promise<T> {
  const { token, json, headers, ...init } = options

  const response = await fetch(path, {
    ...init,
    headers: {
      ...(json !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
    ...(json !== undefined ? { body: JSON.stringify(json) } : {}),
  })

  const rawBody = await response.text()
  const body = rawBody ? (JSON.parse(rawBody) as unknown) : null

  if (!response.ok) {
    const problem = (body ?? {}) as ProblemDetails
    throw new ApiError(response.status, {
      title: problem.title,
      detail: problem.detail,
      fieldErrors: problem.errors,
      // Only a string is carried through: `reason` is an open extension slot, and
      // a caller comparing it against known values should never have to guard
      // against a number or an object arriving in it.
      reason: typeof problem.reason === 'string' ? problem.reason : undefined,
      extensions: problemExtensions(problem),
    })
  }

  return body as T
}
