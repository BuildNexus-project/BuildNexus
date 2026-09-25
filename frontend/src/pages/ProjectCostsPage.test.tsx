import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { ProjectCostsPage } from '@/pages/ProjectCostsPage'
import { apiResponse, stubFetch, type RecordedRequest } from '@/test/fake-fetch'
import type { Role } from '@/lib/roles'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'
const MANAGER_ID = '6f9619ff-8b86-d011-b42d-00cf4fc964ff'
const PROJECT_ID = 'b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b'

function quotation(estimatedTotal: number, createdAtUtc = '2026-08-04T09:00:00Z') {
  return {
    id: crypto.randomUUID(),
    projectId: PROJECT_ID,
    estimatedTotal,
    createdBy: MANAGER_ID,
    createdAtUtc,
  }
}

function invoice(amount: number, status: 'Pending' | 'Paid' = 'Pending') {
  return {
    id: crypto.randomUUID(),
    projectId: PROJECT_ID,
    amount,
    status,
    createdBy: MANAGER_ID,
    createdAtUtc: '2026-08-04T09:00:00Z',
    paidAtUtc: status === 'Paid' ? '2026-08-06T09:00:00Z' : null,
  }
}

function signInAs(role: Role) {
  const claims = {
    sub: MANAGER_ID,
    name: 'Nimal Silva',
    email: 'nimal@example.com',
    role,
    exp: Math.floor(Date.now() / 1000) + 3600,
  }

  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')

  localStorage.setItem(TOKEN_STORAGE_KEY, `header.${payload}.signature`)
}

function renderPage(...responses: Array<Response | Error>): RecordedRequest[] {
  signInAs('ProjectManager')
  const requests = stubFetch(...responses)

  render(
    <MemoryRouter initialEntries={[`/projects/${PROJECT_ID}/costs`]}>
      <AuthProvider>
        <Routes>
          <Route path="/projects/:projectId/costs" element={<ProjectCostsPage />} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('loading a project&apos;s costs', () => {
  it('reads both the quotations and the invoices for the project in the route', async () => {
    const requests = renderPage(
      apiResponse(200, [quotation(1_250_000)]),
      apiResponse(200, [invoice(250_000)]),
    )

    await waitFor(() => expect(requests).toHaveLength(2))

    expect(requests.map((request) => request.path).sort()).toEqual([
      `/api/payments/projects/${PROJECT_ID}/invoices`,
      `/api/payments/projects/${PROJECT_ID}/quotations`,
    ])
  })

  it('shows the current estimate, which is the newest quotation', async () => {
    // The service returns them newest first, so the front of the list is the
    // figure that counts — the page must not re-sort or pick the last.
    renderPage(
      apiResponse(200, [quotation(1_100_000, '2026-08-05T09:00:00Z'), quotation(900_000)]),
      apiResponse(200, []),
    )

    expect(await screen.findByText(/Currently estimated at/)).toHaveTextContent('1,100,000.00')
  })

  it('marks the current quotation and leaves the earlier ones as history', async () => {
    renderPage(
      apiResponse(200, [quotation(1_100_000, '2026-08-05T09:00:00Z'), quotation(900_000)]),
      apiResponse(200, []),
    )

    const current = await screen.findByText('Current')
    const rows = screen.getAllByRole('row')

    // Exactly one row is badged, and it is the first data row.
    expect(screen.getAllByText('Current')).toHaveLength(1)
    expect(within(rows[1]).getByText('Current')).toBe(current)
  })

  it('says so plainly when a project has never been quoted', async () => {
    renderPage(apiResponse(200, []), apiResponse(200, []))

    expect(await screen.findByText(/has not been quoted yet/)).toBeInTheDocument()
    expect(screen.getByText('No quotations yet.')).toBeInTheDocument()
  })

  it('shows what has been billed against the estimate', async () => {
    renderPage(
      apiResponse(200, [quotation(1_000_000)]),
      apiResponse(200, [invoice(250_000), invoice(100_000)]),
    )

    // Both halves of the story's purpose in one line: expected, and current.
    const summary = await screen.findByText(/billed across/)
    expect(summary).toHaveTextContent('350,000.00')
    expect(summary).toHaveTextContent('2 invoices')
    expect(summary).toHaveTextContent('1,000,000.00')
  })

  it('shows a failed read rather than an empty screen', async () => {
    renderPage(
      apiResponse(403, { title: 'Not allowed for your role', detail: 'Your role does not permit this action.' }),
      apiResponse(200, []),
    )

    expect(await screen.findByText('Your role does not permit this action.')).toBeInTheDocument()
  })
})

describe('generating a quotation', () => {
  it('posts the estimated total and re-reads the project&apos;s costs', async () => {
    const requests = renderPage(
      apiResponse(200, []),
      apiResponse(200, []),
      apiResponse(201, quotation(1_250_000)),
      apiResponse(200, [quotation(1_250_000)]),
      apiResponse(200, []),
    )

    await screen.findByText(/has not been quoted yet/)

    await userEvent.type(screen.getByLabelText(/Estimated total/), '1250000')
    await userEvent.click(screen.getByRole('button', { name: 'Generate quotation' }))

    await waitFor(() => expect(screen.getByText(/Currently estimated at/)).toBeInTheDocument())

    const post = requests.find((request) => request.method === 'POST')
    expect(post?.path).toBe(`/api/payments/projects/${PROJECT_ID}/quotations`)
    expect(post?.body).toEqual({ estimatedTotal: 1_250_000 })
  })

  it('refuses a zero total at the form edge, without calling the service', async () => {
    const requests = renderPage(apiResponse(200, []), apiResponse(200, []))

    await screen.findByText(/has not been quoted yet/)

    await userEvent.type(screen.getByLabelText(/Estimated total/), '0')
    await userEvent.click(screen.getByRole('button', { name: 'Generate quotation' }))

    expect(await screen.findByText(/must be greater than zero/)).toBeInTheDocument()
    expect(requests.filter((request) => request.method === 'POST')).toHaveLength(0)
  })

  it("shows the service's reason when it refuses the quotation", async () => {
    renderPage(
      apiResponse(200, []),
      apiResponse(200, []),
      apiResponse(400, {
        title: 'One or more validation errors occurred.',
        errors: { EstimatedTotal: ['The estimated total must be greater than zero.'] },
      }),
    )

    await screen.findByText(/has not been quoted yet/)

    await userEvent.type(screen.getByLabelText(/Estimated total/), '5000')
    await userEvent.click(screen.getByRole('button', { name: 'Generate quotation' }))

    expect(
      await screen.findByText('The estimated total must be greater than zero.'),
    ).toBeInTheDocument()
  })
})

describe('raising an invoice', () => {
  it('posts the amount and sends no status', async () => {
    const requests = renderPage(
      apiResponse(200, [quotation(1_000_000)]),
      apiResponse(200, []),
      apiResponse(201, invoice(250_000)),
      apiResponse(200, [quotation(1_000_000)]),
      apiResponse(200, [invoice(250_000)]),
    )

    await screen.findByText(/Nothing has been billed/)

    await userEvent.type(screen.getByLabelText(/Amount/), '250000')
    await userEvent.click(screen.getByRole('button', { name: 'Raise invoice' }))

    await waitFor(() => expect(screen.getByText(/billed across/)).toBeInTheDocument())

    const post = requests.find((request) => request.method === 'POST')
    expect(post?.path).toBe(`/api/payments/projects/${PROJECT_ID}/invoices`)
    expect(post?.body).toEqual({ amount: 250_000 })
    expect(post?.body).not.toHaveProperty('status')
  })

  it('shows a raised invoice as Pending', async () => {
    renderPage(apiResponse(200, []), apiResponse(200, [invoice(250_000)]))

    expect(await screen.findByText('Pending')).toBeInTheDocument()
  })

  it('shows a settled invoice as Paid', async () => {
    // The status is the service's word, whichever of the two it sends.
    renderPage(apiResponse(200, []), apiResponse(200, [invoice(250_000, 'Paid')]))

    expect(await screen.findByText('Paid')).toBeInTheDocument()
  })

  it('refuses a negative amount at the form edge', async () => {
    const requests = renderPage(apiResponse(200, []), apiResponse(200, []))

    await screen.findByText(/Nothing has been billed/)

    await userEvent.type(screen.getByLabelText(/Amount/), '-5')
    await userEvent.click(screen.getByRole('button', { name: 'Raise invoice' }))

    expect(await screen.findByText(/must be greater than zero/)).toBeInTheDocument()
    expect(requests.filter((request) => request.method === 'POST')).toHaveLength(0)
  })
})
