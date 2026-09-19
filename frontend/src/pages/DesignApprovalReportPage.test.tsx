import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { DesignApprovalReportPage } from '@/pages/DesignApprovalReportPage'
import { apiResponse, stubFetch, type RecordedRequest } from '@/test/fake-fetch'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'
const ADMIN_ID = '1f9619ff-8b86-d011-b42d-00cf4fc964ff'

const APPROVED_PROJECT = '11111111-1111-4111-8111-111111111111'
const IN_PROGRESS_PROJECT = '22222222-2222-4222-8222-222222222222'

function reportRow(overrides: Record<string, unknown> = {}) {
  return {
    projectId: APPROVED_PROJECT,
    documentCount: 2,
    approvedDocumentCount: 2,
    totalVersionCount: 5,
    averageVersionsToApproval: 2.5,
    averageHoursToApproval: 6,
    ...overrides,
  }
}

function signInAsAdmin() {
  const claims = {
    sub: ADMIN_ID,
    name: 'Site Admin',
    email: 'admin@buildnexus.local',
    role: 'Admin',
    exp: Math.floor(Date.now() / 1000) + 3600,
  }

  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')

  localStorage.setItem(TOKEN_STORAGE_KEY, `header.${payload}.signature`)
}

function renderPage(...responses: Array<Response | Error>): RecordedRequest[] {
  signInAsAdmin()
  const requests = stubFetch(...responses)

  render(
    <MemoryRouter>
      <AuthProvider>
        <DesignApprovalReportPage />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

/** The table row containing the given project id. */
async function rowFor(projectId: string) {
  return (await screen.findByText(projectId)).closest('tr') as HTMLElement
}

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('DesignApprovalReportPage', () => {
  it('asks the service for the report and shows a row per project', async () => {
    const requests = renderPage(
      apiResponse(200, [
        reportRow(),
        reportRow({
          projectId: IN_PROGRESS_PROJECT,
          documentCount: 1,
          approvedDocumentCount: 0,
          totalVersionCount: 4,
          averageVersionsToApproval: null,
          averageHoursToApproval: null,
        }),
      ]),
    )

    const approved = await rowFor(APPROVED_PROJECT)
    expect(within(approved).getByText('2 / 2')).toBeInTheDocument()
    expect(within(approved).getByText('5')).toBeInTheDocument()
    expect(within(approved).getByText('2.5')).toBeInTheDocument()
    expect(within(approved).getByText('6.0 h')).toBeInTheDocument()

    expect(requests[0].path).toBe('/api/designs/reports/approval')
  })

  it('shows a dash for the averages where nothing is approved yet', async () => {
    renderPage(
      apiResponse(200, [
        reportRow({
          projectId: IN_PROGRESS_PROJECT,
          documentCount: 1,
          approvedDocumentCount: 0,
          totalVersionCount: 6,
          averageVersionsToApproval: null,
          averageHoursToApproval: null,
        }),
      ]),
    )

    const row = await rowFor(IN_PROGRESS_PROJECT)
    expect(within(row).getByText('0 / 1')).toBeInTheDocument()
    expect(within(row).getByText('6')).toBeInTheDocument()
    expect(within(row).getAllByText('—')).toHaveLength(2)
  })

  it('formats a multi-day time-to-approval in days', async () => {
    renderPage(apiResponse(200, [reportRow({ averageHoursToApproval: 75 })]))

    const row = await rowFor(APPROVED_PROJECT)
    // 75h = 3 days and 3 hours.
    expect(within(row).getByText('3 d 3 h')).toBeInTheDocument()
  })

  it('says so when no project has design documents', async () => {
    renderPage(apiResponse(200, []))

    expect(
      await screen.findByText('No project has any design documents yet.'),
    ).toBeInTheDocument()
  })

  it('shows the not-available card when the service refuses', async () => {
    renderPage(
      apiResponse(403, {
        title: 'Not allowed for your role',
        detail: 'Your role does not permit this action.',
      }),
    )

    expect(await screen.findByText('This report is not available to you')).toBeInTheDocument()
    expect(screen.getByText('Your role does not permit this action.')).toBeInTheDocument()
  })
})
