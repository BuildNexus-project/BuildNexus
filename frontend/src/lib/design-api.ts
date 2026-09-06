import type { ApiFetchOptions } from './api'
import { ApiError } from './api'

/** The token-attaching fetch handed out by the auth context. */
type AuthFetch = <T>(path: string, options?: Omit<ApiFetchOptions, 'token'>) => Promise<T>

/**
 * Where a design document version sits in review. US-09 defines one value —
 * every upload lands as `Submitted`.
 */
export type DesignDocumentStatus = 'Submitted'

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
