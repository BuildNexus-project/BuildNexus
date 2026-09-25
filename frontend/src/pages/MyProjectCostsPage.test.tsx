import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { MyProjectCostsPage } from '@/pages/MyProjectCostsPage'
import { apiResponse, stubFetch, type RecordedRequest } from '@/test/fake-fetch'
import type { Role } from '@/lib/roles'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'
const CLIENT_ID = '6f9619ff-8b86-d011-b42d-00cf4fc964ff'
const VILLA_ID = 'b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b'
const TOWNHOUSE_ID = 'c3e5a7b9-2d4f-4e6a-9b0c-1d2e3f4a5b6c'

function projects() {
  return [
    {
      id: VILLA_ID,
      clientId: CLIENT_ID,
      name: 'Beachfront villa',
      location: 'Galle',
      status: 'Construction',
      createdAt: '2026-08-01T09:00:00',
      updatedAt: '2026-08-04T09:00:00',
    },
    {
      id: TOWNHOUSE_ID,
      clientId: CLIENT_ID,
      name: 'City townhouse',
      location: 'Colombo',
      status: 'Pending',
      createdAt: '2026-07-20T09:00:00',
      updatedAt: '2026-08-02T09:00:00',
    },
  ]
}

function quotation(projectId: string, estimatedTotal: number, createdAtUtc = '2026-08-04T09:00:00Z') {
  return {
    id: crypto.randomUUID(),
    projectId,
    estimatedTotal,
    createdBy: CLIENT_ID,
    createdAtUtc,
  }
}

function invoice(projectId: string, amount: number, status: 'Pending' | 'Paid' = 'Pending') {
  return {
    id: crypto.randomUUID(),
    projectId,
    amount,
    status,
    createdBy: CLIENT_ID,
    createdAtUtc: '2026-08-05T09:00:00Z',
    paidAtUtc: status === 'Paid' ? '2026-08-06T09:00:00Z' : null,
  }
}

function signInAs(role: Role) {
  const claims = {
    sub: CLIENT_ID,
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

function renderPage(...responses: Array<Response | Error>): RecordedRequest[] {
  signInAs('Client')
  const requests = stubFetch(...responses)

  render(
    <MemoryRouter>
      <AuthProvider>
        <MyProjectCostsPage />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

/** The villa's card, so an assertion cannot accidentally match the other project. */
async function villaCard(): Promise<HTMLElement> {
  const heading = await screen.findByText('Beachfront villa')
  // `closest` is typed as returning Element; `within` needs an HTMLElement, and
  // every card here is one.
  const card = heading.closest<HTMLElement>('[data-slot="card"]')

  if (!card) {
    throw new Error('The villa project did not render inside a card.')
  }

  return card
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('reading the Client&apos;s costs', () => {
  it('uses the ownership-scoped routes, not the staff ones', async () => {
    // The service scopes /my-projects to the caller's own projects. Going
    // through the staff path would be both a 403 and the wrong gate.
    const requests = renderPage(
      apiResponse(200, projects()),
      apiResponse(200, []),
      apiResponse(200, []),
      apiResponse(200, []),
      apiResponse(200, []),
    )

    await waitFor(() => expect(requests.length).toBeGreaterThan(1))

    const costPaths = requests.map((request) => request.path).filter((path) => path.includes('quotations') || path.includes('invoices'))

    expect(costPaths.length).toBeGreaterThan(0)
    expect(costPaths.every((path) => path.startsWith('/api/payments/my-projects/'))).toBe(true)
  })

  it('asks about every project, not only the ones under construction', async () => {
    // A quotation can be raised long before a build starts — a Pending project
    // is exactly the one a Client most wants a price for.
    const requests = renderPage(
      apiResponse(200, projects()),
      apiResponse(200, []),
      apiResponse(200, []),
      apiResponse(200, []),
      apiResponse(200, []),
    )

    await waitFor(() => expect(requests).toHaveLength(5))

    expect(requests.some((request) => request.path.includes(TOWNHOUSE_ID))).toBe(true)
  })

  it('shows the current estimate, which is the newest quotation', async () => {
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, [
        quotation(VILLA_ID, 1_100_000, '2026-08-05T09:00:00Z'),
        quotation(VILLA_ID, 900_000),
      ]),
      apiResponse(200, []),
    )

    const card = await villaCard()

    expect(within(card).getByText(/1,100,000\.00/)).toBeInTheDocument()
    expect(within(card).queryByText(/900,000\.00/)).not.toBeInTheDocument()
  })

  it('says the price has been revised rather than silently showing a different number', async () => {
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, [
        quotation(VILLA_ID, 1_100_000, '2026-08-05T09:00:00Z'),
        quotation(VILLA_ID, 900_000),
      ]),
      apiResponse(200, []),
    )

    expect(await screen.findByText(/revised 1 time/)).toBeInTheDocument()
  })

  it('tells a Client with no quotation that one is coming, rather than showing nothing', async () => {
    renderPage(apiResponse(200, [projects()[0]]), apiResponse(200, []), apiResponse(200, []))

    expect(await screen.findByText(/Not quoted yet/)).toBeInTheDocument()
  })

  it('totals what has been billed and lists each invoice with its status', async () => {
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, [quotation(VILLA_ID, 1_000_000)]),
      apiResponse(200, [invoice(VILLA_ID, 250_000), invoice(VILLA_ID, 100_000, 'Paid')]),
    )

    const card = await villaCard()

    expect(within(card).getByText(/350,000\.00/)).toBeInTheDocument()
    expect(within(card).getByText('Pending')).toBeInTheDocument()
    expect(within(card).getByText('Paid')).toBeInTheDocument()
  })

  it('says so plainly when nothing has been billed', async () => {
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, [quotation(VILLA_ID, 1_000_000)]),
      apiResponse(200, []),
    )

    expect(await screen.findByText(/Nothing has been billed/)).toBeInTheDocument()
  })

  it('contains one project&apos;s failure to its own card', async () => {
    // A single project the service is unhappy about must not blank the page.
    renderPage(
      apiResponse(200, projects()),
      apiResponse(403, { title: 'Not your project.', detail: 'You can only view quotations for your own projects.' }),
      apiResponse(200, []),
      apiResponse(200, [quotation(TOWNHOUSE_ID, 500_000)]),
      apiResponse(200, []),
    )

    // The other project still renders its figure.
    expect(await screen.findByText(/500,000\.00/)).toBeInTheDocument()
    expect(
      screen.getByText('You can only view quotations for your own projects.'),
    ).toBeInTheDocument()
  })

  it("shows the service's own reason when the projects read fails", async () => {
    // A refusal is a real answer with a reason in it, and showing that beats
    // flattening every failure into one house message.
    renderPage(apiResponse(500, { title: 'Server error', detail: 'The project service is down.' }))

    expect(await screen.findByText('The project service is down.')).toBeInTheDocument()
  })

  it('falls back to a house message when a failure carries no reason', async () => {
    // A dropped connection is not an ApiError and has nothing to show, so the
    // page must still say something rather than sitting on "Loading…".
    renderPage(new TypeError('Failed to fetch'))

    expect(await screen.findByText(/Could not load your projects/)).toBeInTheDocument()
  })

  it('tells a Client with no projects that they have none', async () => {
    renderPage(apiResponse(200, []))

    expect(await screen.findByText('You have no projects yet.')).toBeInTheDocument()
  })
})
