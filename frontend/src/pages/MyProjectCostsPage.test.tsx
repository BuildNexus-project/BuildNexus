import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
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

/**
 * The payment-history answer the page now reads (US-17): the invoices with
 * their payments, and the balance derived from them the way the service does.
 */
function history(...entries: Array<ReturnType<typeof invoice> | ReturnType<typeof paid>>) {
  const invoices = entries.map((entry) => ('invoice' in entry ? entry : withPayments(entry)))

  return {
    projectId: VILLA_ID,
    invoices,
    totalInvoiced: invoices.reduce((total, entry) => total + entry.invoice.amount, 0),
    totalPaid: invoices.reduce((total, entry) => total + entry.amountPaid, 0),
    outstandingBalance: invoices.reduce((total, entry) => total + entry.outstandingAmount, 0),
  }
}

/** An invoice with nothing paid against it. */
function withPayments(one: ReturnType<typeof invoice>) {
  return {
    invoice: one,
    payments: [],
    amountPaid: 0,
    outstandingAmount: one.status === 'Paid' ? 0 : one.amount,
  }
}

/** An invoice with payments against it, for the history assertions. */
function paid(one: ReturnType<typeof invoice>, ...amounts: number[]) {
  const amountPaid = amounts.reduce((total, amount) => total + amount, 0)

  return {
    invoice: one,
    payments: amounts.map((amount, index) => ({
      id: crypto.randomUUID(),
      invoiceId: one.id,
      amount,
      paidBy: CLIENT_ID,
      recordedAtUtc: `2026-08-0${index + 5}T09:00:00Z`,
    })),
    amountPaid,
    outstandingAmount: one.amount - amountPaid,
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
      apiResponse(200, history()),
      apiResponse(200, []),
      apiResponse(200, history()),
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
      apiResponse(200, history()),
      apiResponse(200, []),
      apiResponse(200, history()),
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
    renderPage(apiResponse(200, [projects()[0]]), apiResponse(200, []), apiResponse(200, history()))

    expect(await screen.findByText(/Not quoted yet/)).toBeInTheDocument()
  })

  it('totals what has been billed and lists each invoice with its status', async () => {
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, [quotation(VILLA_ID, 1_000_000)]),
      apiResponse(200, history(invoice(VILLA_ID, 250_000), invoice(VILLA_ID, 100_000, 'Paid'))),
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
      apiResponse(200, history()),
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

describe('paying an invoice', () => {
  it('posts the amount to the invoice being paid and re-reads the costs', async () => {
    const requests = renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, [quotation(VILLA_ID, 1_000_000)]),
      apiResponse(200, history(invoice(VILLA_ID, 1_000))),
      // the payment
      apiResponse(201, {
        payment: {
          id: crypto.randomUUID(),
          invoiceId: VILLA_ID,
          amount: 400,
          paidBy: CLIENT_ID,
          recordedAtUtc: '2026-09-25T10:00:00Z',
        },
        outstandingAmount: 600,
        invoiceStatus: 'Pending',
      }),
      // the re-read
      apiResponse(200, [projects()[0]]),
      apiResponse(200, [quotation(VILLA_ID, 1_000_000)]),
      apiResponse(200, history(invoice(VILLA_ID, 1_000))),
    )

    await screen.findByText(/Pending/)

    await userEvent.type(screen.getByLabelText(/Payment amount/), '400')
    await userEvent.click(screen.getByRole('button', { name: 'Pay' }))

    await waitFor(() => {
      const post = requests.find((request) => request.method === 'POST')
      expect(post?.body).toEqual({ amount: 400 })
    })

    const post = requests.find((request) => request.method === 'POST')
    expect(post?.path).toContain('/payments')
  })

  it('offers no pay form on an invoice that is already settled', async () => {
    // Nothing left to pay, and the service would refuse it anyway.
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(invoice(VILLA_ID, 1_000, 'Paid'))),
    )

    expect(await screen.findByText('Paid')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Pay' })).not.toBeInTheDocument()
  })

  it('refuses a zero amount at the form edge without calling the service', async () => {
    const requests = renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(invoice(VILLA_ID, 1_000))),
    )

    await screen.findByText('Pending')

    await userEvent.type(screen.getByLabelText(/Payment amount/), '0')
    await userEvent.click(screen.getByRole('button', { name: 'Pay' }))

    expect(await screen.findByText(/must be greater than zero/)).toBeInTheDocument()
    expect(requests.filter((request) => request.method === 'POST')).toHaveLength(0)
  })

  it("offers the payable figure when the service refuses an over-payment", async () => {
    // AC-1 as the Client experiences it: told they cannot pay that much, and
    // handed the amount that would have worked.
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(invoice(VILLA_ID, 1_000))),
      apiResponse(409, {
        title: 'Payment exceeds the outstanding amount.',
        detail: 'This invoice has 600.00 outstanding. Enter that amount or less.',
        reason: 'ExceedsOutstanding',
        outstandingAmount: 600,
      }),
    )

    await screen.findByText('Pending')

    await userEvent.type(screen.getByLabelText(/Payment amount/), '700')
    await userEvent.click(screen.getByRole('button', { name: 'Pay' }))

    expect(
      await screen.findByText('This invoice has 600.00 outstanding. Enter that amount or less.'),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Pay .*600/ })).toBeInTheDocument()
  })

  it('fills the form with the payable figure when the correction is taken', async () => {
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(invoice(VILLA_ID, 1_000))),
      apiResponse(409, {
        title: 'Payment exceeds the outstanding amount.',
        detail: 'This invoice has 600.00 outstanding.',
        reason: 'ExceedsOutstanding',
        outstandingAmount: 600,
      }),
    )

    await screen.findByText('Pending')

    await userEvent.type(screen.getByLabelText(/Payment amount/), '700')
    await userEvent.click(screen.getByRole('button', { name: 'Pay' }))
    await userEvent.click(await screen.findByRole('button', { name: /Pay .*600/ }))

    expect(screen.getByLabelText(/Payment amount/)).toHaveValue(600)
  })

  it('shows a settled invoice as Paid after the re-read', async () => {
    // AC-2 as the Client sees it.
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(invoice(VILLA_ID, 1_000))),
      apiResponse(201, {
        payment: {
          id: crypto.randomUUID(),
          invoiceId: VILLA_ID,
          amount: 1_000,
          paidBy: CLIENT_ID,
          recordedAtUtc: '2026-09-25T10:00:00Z',
        },
        outstandingAmount: 0,
        invoiceStatus: 'Paid',
      }),
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(invoice(VILLA_ID, 1_000, 'Paid'))),
    )

    await screen.findByText('Pending')

    await userEvent.type(screen.getByLabelText(/Payment amount/), '1000')
    await userEvent.click(screen.getByRole('button', { name: 'Pay' }))

    expect(await screen.findByText('Paid')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Pay' })).not.toBeInTheDocument()
  })

  it("shows the service's reason when a payment is refused for any other cause", async () => {
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(invoice(VILLA_ID, 1_000))),
      apiResponse(403, {
        title: 'Not your invoice.',
        detail: 'You can only record payments against invoices on your own projects.',
      }),
    )

    await screen.findByText('Pending')

    await userEvent.type(screen.getByLabelText(/Payment amount/), '100')
    await userEvent.click(screen.getByRole('button', { name: 'Pay' }))

    expect(
      await screen.findByText(
        'You can only record payments against invoices on your own projects.',
      ),
    ).toBeInTheDocument()
  })
})

describe('the payment history and outstanding balance (US-17)', () => {
  it('reads the history endpoint rather than the plain invoice list', async () => {
    // AC-2 wants the balance on the same view as the history, so the page asks
    // for both in one request instead of composing them itself.
    const requests = renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history()),
    )

    await waitFor(() => expect(requests).toHaveLength(3))

    expect(requests.map((request) => request.path)).toContain(
      `/api/payments/my-projects/${VILLA_ID}/history`,
    )
  })

  it('shows the outstanding balance the service reported', async () => {
    // AC-2. Not recomputed on the page — the service's figure is the one the
    // pay endpoint enforces, and a second opinion could disagree with it.
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(paid(invoice(VILLA_ID, 1_000), 400), invoice(VILLA_ID, 500))),
    )

    const card = await villaCard()

    // 1000 + 500 billed, 400 paid, so 1100 owed.
    expect(within(card).getByLabelText(/Outstanding balance/)).toHaveTextContent('1,100.00')
  })

  it('lists every invoice with its status, most recent first', async () => {
    // AC-1. The order is the service's answer and the page renders it as given.
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(invoice(VILLA_ID, 900), invoice(VILLA_ID, 100, 'Paid'))),
    )

    const card = await villaCard()
    const amounts = within(card)
      .getAllByText(/^LKR/)
      .map((node) => node.textContent)

    expect(amounts.join(' ')).toMatch(/900\.00[\s\S]*100\.00/)
    expect(within(card).getByText('Pending')).toBeInTheDocument()
    expect(within(card).getByText('Paid')).toBeInTheDocument()
  })

  it('shows each payment made against its own invoice', async () => {
    // AC-1's "linked payments".
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(paid(invoice(VILLA_ID, 1_000), 250, 150))),
    )

    const card = await villaCard()

    expect(within(card).getByText(/Paid LKR\s?250\.00/)).toBeInTheDocument()
    expect(within(card).getByText(/Paid LKR\s?150\.00/)).toBeInTheDocument()
  })

  it('says how much of a part-paid invoice is still owed', async () => {
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(paid(invoice(VILLA_ID, 1_000), 400))),
    )

    const card = await villaCard()

    expect(within(card).getByText(/600\.00 still owed/)).toBeInTheDocument()
  })

  it('offers no pay form once an invoice has nothing outstanding', async () => {
    // Driven by the remaining amount rather than the status label, so an
    // invoice whose payments cover it cannot still show a pay box.
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(paid(invoice(VILLA_ID, 500, 'Paid'), 500))),
    )

    await screen.findByText('Paid')

    expect(screen.queryByRole('button', { name: 'Pay' })).not.toBeInTheDocument()
  })

  it('keeps the payment history visible on a settled invoice', async () => {
    // The Client still wants to see what they paid after it is settled.
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(paid(invoice(VILLA_ID, 500, 'Paid'), 500))),
    )

    const card = await villaCard()

    expect(within(card).getByText(/Paid LKR\s?500\.00/)).toBeInTheDocument()
  })

  it('shows a zero balance on a fully settled project', async () => {
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(paid(invoice(VILLA_ID, 500, 'Paid'), 500))),
    )

    const card = await villaCard()

    expect(within(card).getByLabelText(/Outstanding balance/)).toHaveTextContent('0.00')
  })

  it('updates the balance immediately after a payment is recorded', async () => {
    // AC-2's "updates immediately": recording a payment re-reads the history,
    // so the balance moves without the Client refreshing anything.
    renderPage(
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(invoice(VILLA_ID, 1_000))),
      // the payment
      apiResponse(201, {
        payment: {
          id: crypto.randomUUID(),
          invoiceId: VILLA_ID,
          amount: 400,
          paidBy: CLIENT_ID,
          recordedAtUtc: '2026-09-25T10:00:00Z',
        },
        outstandingAmount: 600,
        invoiceStatus: 'Pending',
      }),
      // the re-read the payment triggers
      apiResponse(200, [projects()[0]]),
      apiResponse(200, []),
      apiResponse(200, history(paid(invoice(VILLA_ID, 1_000), 400))),
    )

    const before = await villaCard()
    expect(within(before).getByLabelText(/Outstanding balance/)).toHaveTextContent('1,000.00')

    await userEvent.type(screen.getByLabelText(/Payment amount/), '400')
    await userEvent.click(screen.getByRole('button', { name: 'Pay' }))

    await waitFor(async () => {
      expect(within(await villaCard()).getByLabelText(/Outstanding balance/)).toHaveTextContent(
        '600.00',
      )
    })
  })
})
