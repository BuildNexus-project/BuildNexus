import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { DesignDocumentsPage } from '@/pages/DesignDocumentsPage'
import type { Role } from '@/lib/roles'
import { apiResponse, fileResponse, stubFetch, type RecordedRequest } from '@/test/fake-fetch'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'
const PROJECT_ID = 'b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b'
const CLIENT_ID = '6f9619ff-8b86-d011-b42d-00cf4fc964ff'
const ARCHITECT_ID = '11111111-1111-4111-8111-111111111111'
const VERSION_ID = 'aaaaaaaa-0000-4000-8000-000000000001'

/** One document with one version, as the service returns it. */
function designDocument(overrides: Record<string, unknown> = {}) {
  return {
    id: 'dddddddd-0000-4000-8000-000000000001',
    projectId: PROJECT_ID,
    name: 'GroundFloorPlan',
    createdBy: ARCHITECT_ID,
    createdAt: '2026-09-01T09:00:00',
    latestVersionNumber: 1,
    versions: [
      {
        id: VERSION_ID,
        documentId: 'dddddddd-0000-4000-8000-000000000001',
        documentName: 'GroundFloorPlan',
        projectId: PROJECT_ID,
        versionNumber: 1,
        displayName: 'GroundFloorPlan_v1',
        fileName: 'plan.pdf',
        contentType: 'application/pdf',
        fileSizeBytes: 1024,
        status: 'Submitted',
        revisionComment: null,
        uploadedBy: ARCHITECT_ID,
        uploadedAt: '2026-09-01T09:00:00',
      },
    ],
    ...overrides,
  }
}

/** The version the service hands back from a successful upload. */
function uploadedVersion(overrides: Record<string, unknown> = {}) {
  return {
    id: VERSION_ID,
    documentId: 'dddddddd-0000-4000-8000-000000000001',
    documentName: 'GroundFloorPlan',
    projectId: PROJECT_ID,
    versionNumber: 1,
    displayName: 'GroundFloorPlan_v1',
    fileName: 'plan.pdf',
    contentType: 'application/pdf',
    fileSizeBytes: 9,
    status: 'Submitted',
    revisionComment: 'First cut',
    uploadedBy: ARCHITECT_ID,
    uploadedAt: '2026-09-01T09:00:00',
    ...overrides,
  }
}

/**
 * Stores a token shaped like the one the User Service issues, minus a real
 * signature — the app never verifies one and cannot, so this takes the same
 * path through `decodeToken` as a real token would.
 */
function signInAs(role: Role, userId: string) {
  const claims = {
    sub: userId,
    name: 'Ada Perera',
    email: 'ada@example.com',
    role,
    exp: Math.floor(Date.now() / 1000) + 3600,
  }

  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')

  localStorage.setItem(TOKEN_STORAGE_KEY, `header.${payload}.signature`)
}

function renderPage(
  who: { role: Role; userId: string },
  ...responses: Array<Response | Error>
): RecordedRequest[] {
  signInAs(who.role, who.userId)
  const requests = stubFetch(...responses)

  render(
    <MemoryRouter initialEntries={[`/projects/${PROJECT_ID}/designs`]}>
      <AuthProvider>
        <Routes>
          <Route path="/projects/:projectId/designs" element={<DesignDocumentsPage />} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

const asOwningClient = { role: 'Client' as Role, userId: CLIENT_ID }
const asArchitect = { role: 'Architect' as Role, userId: ARCHITECT_ID }

/**
 * Fills the text fields and attaches a file by firing `change` directly,
 * rather than through `userEvent.upload` — which simulates the native file
 * picker and silently drops anything the input's `accept` attribute does not
 * match. Going around that is what lets the "wrong type" tests reach the
 * client-side MIME check at all, the same way a drag-and-drop would in a real
 * browser.
 */
async function fillAndPickFile(name: string, file: File, revisionComment?: string) {
  await screen.findByLabelText('Document name')

  fireEvent.change(screen.getByLabelText('Document name'), { target: { value: name } })

  if (revisionComment !== undefined) {
    fireEvent.change(screen.getByLabelText('Revision comment'), { target: { value: revisionComment } })
  }

  fireEvent.change(screen.getByLabelText('File'), { target: { files: [file] } })
}

function submit() {
  fireEvent.click(screen.getByRole('button', { name: 'Upload' }))
}

beforeEach(() => {
  // jsdom does not implement the object-URL API a real browser has; the page
  // only needs it called, not a working URL back.
  URL.createObjectURL = vi.fn(() => 'blob:mock-url')
  URL.revokeObjectURL = vi.fn()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('DesignDocumentsPage', () => {
  it('lists every version for a Client, with no upload form', async () => {
    renderPage(asOwningClient, apiResponse(200, [designDocument()]))

    expect(await screen.findByText('GroundFloorPlan_v1')).toBeInTheDocument()
    expect(screen.getByText('Submitted')).toBeInTheDocument()
    expect(screen.queryByLabelText('Document name')).not.toBeInTheDocument()
  })

  it('says so when the project has nothing uploaded yet', async () => {
    renderPage(asOwningClient, apiResponse(200, []))

    expect(
      await screen.findByText('No design documents have been uploaded for this project yet.'),
    ).toBeInTheDocument()
  })

  it('shows the current caller’s id and role has nothing to do with which versions are shown', async () => {
    // Two versions, oldest first, the way the service sends them — the latest
    // revision is the last row.
    renderPage(
      asOwningClient,
      apiResponse(200, [
        designDocument({
          latestVersionNumber: 2,
          versions: [
            designDocument().versions[0],
            { ...designDocument().versions[0], id: 'v2', versionNumber: 2, displayName: 'GroundFloorPlan_v2' },
          ],
        }),
      ]),
    )

    expect(await screen.findByText('GroundFloorPlan_v1')).toBeInTheDocument()
    expect(screen.getByText('GroundFloorPlan_v2')).toBeInTheDocument()
  })

  it('shows an Architect the upload form', async () => {
    renderPage(asArchitect, apiResponse(200, []))

    expect(await screen.findByLabelText('Document name')).toBeInTheDocument()
    expect(screen.getByLabelText('Revision comment')).toBeInTheDocument()
    expect(screen.getByLabelText('File')).toBeInTheDocument()
  })

  it('sends the upload as multipart form data and reloads the list from the reply', async () => {
    const requests = renderPage(
      asArchitect,
      apiResponse(200, []), // initial list — nothing yet
      apiResponse(201, uploadedVersion()), // the upload itself
      apiResponse(200, [designDocument()]), // reloaded after it
    )

    const file = new File(['%PDF-1.4'], 'plan.pdf', { type: 'application/pdf' })
    await fillAndPickFile('GroundFloorPlan', file, 'First cut')
    submit()

    await waitFor(() => expect(requests).toHaveLength(3))

    expect(requests[1].path).toBe(`/api/designs/projects/${PROJECT_ID}/documents`)
    expect(requests[1].method).toBe('POST')

    const body = requests[1].body as Record<string, unknown>
    expect(body.name).toBe('GroundFloorPlan')
    expect(body.revisionComment).toBe('First cut')
    expect(body.file).toBeInstanceOf(File)
    expect((body.file as File).name).toBe('plan.pdf')

    // The service's reply is what is shown, not a locally-built guess.
    expect(await screen.findByText('GroundFloorPlan_v1')).toBeInTheDocument()
  })

  it('leaves out the revision comment field when it was left blank', async () => {
    const requests = renderPage(
      asArchitect,
      apiResponse(200, []), // initial list
      apiResponse(201, uploadedVersion({ revisionComment: null })), // the upload itself
      apiResponse(200, []), // reloaded after it
    )

    const file = new File(['%PDF-1.4'], 'plan.pdf', { type: 'application/pdf' })
    await fillAndPickFile('GroundFloorPlan', file)
    submit()

    await waitFor(() => expect(requests).toHaveLength(3))
    expect(requests[1].body).not.toHaveProperty('revisionComment')
  })

  it('refuses a file that is not PDF, JPG or PNG before sending anything', async () => {
    const requests = renderPage(asArchitect, apiResponse(200, []))

    const file = new File(['just some text'], 'notes.txt', { type: 'text/plain' })
    await fillAndPickFile('Notes', file)
    submit()

    expect(
      await screen.findByText('Only PDF, JPG and PNG files are accepted.'),
    ).toBeInTheDocument()
    // Only the initial list load — the bad file never went anywhere.
    expect(requests).toHaveLength(1)
  })

  it('refuses a file over the 10 MB limit before sending anything', async () => {
    const requests = renderPage(asArchitect, apiResponse(200, []))

    const big = new File(['x'], 'big.pdf', { type: 'application/pdf' })
    Object.defineProperty(big, 'size', { value: 10 * 1024 * 1024 + 1 })
    await fillAndPickFile('Big', big)
    submit()

    expect(
      await screen.findByText('The file is larger than the 10 MB limit.'),
    ).toBeInTheDocument()
    expect(requests).toHaveLength(1)
  })

  it('refuses a form with no file chosen', async () => {
    const requests = renderPage(asArchitect, apiResponse(200, []))

    await screen.findByLabelText('Document name')
    fireEvent.change(screen.getByLabelText('Document name'), { target: { value: 'GroundFloorPlan' } })
    submit()

    expect(
      await screen.findByText('Choose a PDF, JPG or PNG file to upload.'),
    ).toBeInTheDocument()
    expect(requests).toHaveLength(1)
  })

  it('shows the service’s own refusal when it rejects an upload the form let through', async () => {
    const requests = renderPage(
      asArchitect,
      apiResponse(200, []),
      apiResponse(400, {
        title: 'The file was not accepted',
        detail: 'Only PDF, JPG and PNG files are accepted.',
      }),
    )

    const file = new File(['%PDF-1.4'], 'plan.pdf', { type: 'application/pdf' })
    await fillAndPickFile('GroundFloorPlan', file)
    submit()

    expect(
      await screen.findByText('Only PDF, JPG and PNG files are accepted.'),
    ).toBeInTheDocument()
    expect(requests).toHaveLength(2)
  })

  it('downloads a version’s file when its Download button is clicked', async () => {
    const requests = renderPage(
      asOwningClient,
      apiResponse(200, [designDocument()]),
      fileResponse(200, new Blob(['%PDF-1.4'], { type: 'application/pdf' })),
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Download' }))

    await waitFor(() => expect(requests).toHaveLength(2))
    expect(requests[1].path).toBe(`/api/designs/versions/${VERSION_ID}/file`)
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1)
    expect(URL.revokeObjectURL).toHaveBeenCalledTimes(1)
  })

  it('shows a failure that names no field at form level', async () => {
    renderPage(
      asOwningClient,
      apiResponse(403, {
        title: 'Not your project',
        detail: 'Only the client who submitted this project, the staff assigned to it, or an administrator can work with its design documents.',
      }),
    )

    expect(
      await screen.findByText(
        'Only the client who submitted this project, the staff assigned to it, or an administrator can work with its design documents.',
      ),
    ).toBeInTheDocument()
  })
})
