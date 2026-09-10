import type { ApiFetchOptions } from './api'
import { ApiError } from './api'

/** The token-attaching fetch handed out by the auth context. */
type AuthFetch = <T>(path: string, options?: Omit<ApiFetchOptions, 'token'>) => Promise<T>

/**
 * Where a design document version sits in review. Every upload lands as
 * `Submitted`; a Client's review (US-11) moves it straight to `Approved` or
 * `RevisionRequested`. `UnderReview` stays reserved for a future "picked up
 * for review" step — nothing sets it yet.
 */
export type DesignDocumentStatus = 'Submitted' | 'UnderReview' | 'Approved' | 'RevisionRequested'

/**
 * One uploaded version of a design document, with the metadata US-09 keeps
 * beside it.
 *
 * `displayName` is the `GroundFloorPlan_v2` label the service builds from the
 * document's name and this version's number — used as-is rather than rebuilt
 * here.
 */
export type DesignVersion = {
  id: string
  documentId: string
  documentName: string
  projectId: string
  versionNumber: number
  displayName: string
  fileName: string
  contentType: string
  fileSizeBytes: number
  status: DesignDocumentStatus
  /** `null` when the Architect had nothing to say for this revision. */
  revisionComment: string | null
  /** The Architect who uploaded it, as an id — the account lives in the User Service. */
  uploadedBy: string
  /** ISO-8601, as the service serialises it. */
  uploadedAt: string
  /**
   * True for the single latest version of this document that is
   * `UnderReview` or `Approved` — never for `Submitted`. False for every
   * version when none has reached either state yet, rather than a `Submitted`
   * version standing in for one.
   */
  isCurrent: boolean
  /** The Client who approved this version or asked for a revision on it, or `null` before either has happened. */
  reviewedBy: string | null
  /** ISO-8601, or `null` until `reviewedBy` is set — the two always arrive together. */
  reviewedAt: string | null
  /**
   * The Client's note on what needs to change — the whole point of showing
   * this to the Architect. Set only when the decision was a request for
   * revision.
   */
  reviewComment: string | null
}

/**
 * What a review action (US-11) recorded: the version, the decision, who made
 * it, when, and — for a revision request — what they asked for.
 */
export type ReviewDecision = {
  versionId: string
  documentId: string
  displayName: string
  /** `'Approved'` or `'RevisionRequested'`. */
  status: DesignDocumentStatus
  reviewedBy: string
  reviewedAt: string
  reviewComment: string | null
}

/**
 * One design document and every version uploaded under it, oldest first — so
 * the latest work is the last entry and the whole history sits beside it.
 */
export type DesignDocument = {
  id: string
  projectId: string
  name: string
  createdBy: string
  createdAt: string
  latestVersionNumber: number
  versions: DesignVersion[]
}

/**
 * What an Architect submits for one upload.
 *
 * There is no project or uploader here: the project is in the URL and the
 * uploader comes from the token, so an upload cannot claim to be for another
 * project or by another person. A blank `revisionComment` is sent as nothing.
 */
export type UploadDesignDocumentPayload = {
  /** The document to file this under. A new name starts a new document at v1. */
  name: string
  revisionComment: string
  file: File
}

/**
 * Uploads a new version of a design document for a project.
 *
 * Architect only, and only for a project they are on — the service asks the
 * Project Service with the caller's own token. Any other role gets
 * {@link ApiError} with status 403, a project that is not theirs a 403, an
 * unknown project a 404, and a file that is not a PDF/JPG/PNG or is over 10 MB
 * a 400 whose `detail` says which.
 *
 * Sent as `multipart/form-data` — the browser sets the boundary, so no
 * `Content-Type` is passed here.
 */
export function uploadDesignDocument(
  authFetch: AuthFetch,
  projectId: string,
  payload: UploadDesignDocumentPayload,
) {
  const form = new FormData()
  form.append('name', payload.name)

  if (payload.revisionComment.trim().length > 0) {
    form.append('revisionComment', payload.revisionComment)
  }

  form.append('file', payload.file)

  return authFetch<DesignVersion>(`/api/designs/projects/${projectId}/documents`, {
    method: 'POST',
    body: form,
  })
}

/**
 * Approves a design document version.
 *
 * Client only, and only for a version whose project they are on. Any other
 * role gets {@link ApiError} with status 403, a project that is not theirs a
 * 403, an unknown version a 404, and a version already decided — or belonging
 * to a document with another version already approved — a 409.
 */
export function approveDesignVersion(authFetch: AuthFetch, versionId: string) {
  return authFetch<ReviewDecision>(`/api/designs/versions/${versionId}/approve`, {
    method: 'POST',
  })
}

/**
 * Asks for changes on a design document version, with a comment on what needs
 * to change.
 *
 * Same access rules as {@link approveDesignVersion}. No document is ever
 * marked current by this outcome — only an approval qualifies.
 */
export function requestDesignRevision(authFetch: AuthFetch, versionId: string, comment: string) {
  return authFetch<ReviewDecision>(`/api/designs/versions/${versionId}/request-revision`, {
    method: 'POST',
    json: { comment },
  })
}

/**
 * The design documents for a project, each with its versions, oldest first.
 *
 * Open to every role, with the service deciding — via the Project Service —
 * whether this caller may see this project at all. A caller who is not party to
 * it gets {@link ApiError} with status 403, and an unknown project a 404.
 */
export function fetchProjectDesignDocuments(authFetch: AuthFetch, projectId: string) {
  return authFetch<DesignDocument[]>(`/api/designs/projects/${projectId}/documents`)
}

/**
 * Downloads the file stored for one version.
 *
 * Goes straight through `fetch` rather than the JSON client: the body is the
 * file's bytes, not JSON. The caller already holds the {@link DesignVersion}, so
 * it supplies the filename for the save dialog.
 *
 * Throws {@link ApiError} for a non-2xx response — 403 if the caller is not on
 * the version's project, 404 if there is no such version.
 */
export async function downloadDesignVersionFile(
  token: string | null,
  versionId: string,
): Promise<Blob> {
  const response = await fetch(`/api/designs/versions/${versionId}/file`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  })

  if (!response.ok) {
    const raw = await response.text()

    let problem: { title?: string; detail?: string } = {}
    try {
      problem = raw ? (JSON.parse(raw) as { title?: string; detail?: string }) : {}
    } catch {
      // A non-JSON error body has nothing to show; the status still stands.
    }

    throw new ApiError(response.status, { title: problem.title, detail: problem.detail })
  }

  return response.blob()
}

/**
 * One project's row in the design approval report (US-20): how much revision
 * its design documents went through, and how long they took to approve —
 * aggregated across the project's documents.
 *
 * `averageVersionsToApproval` and `averageHoursToApproval` are `null` until the
 * project has at least one approved document; a project can be a row with a
 * high `totalVersionCount` and no approval time, which is the bottleneck the
 * report exists to surface.
 */
export type DesignApprovalReportRow = {
  projectId: string
  documentCount: number
  approvedDocumentCount: number
  totalVersionCount: number
  averageVersionsToApproval: number | null
  averageHoursToApproval: number | null
}

/**
 * The design approval report — one row per project that has any design
 * document, ordered by project id.
 *
 * Admin only; any other role gets {@link ApiError} with status 403.
 */
export function fetchDesignApprovalReport(authFetch: AuthFetch) {
  return authFetch<DesignApprovalReportRow[]>('/api/designs/reports/approval')
}
