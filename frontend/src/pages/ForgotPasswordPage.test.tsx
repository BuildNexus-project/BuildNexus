import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { ForgotPasswordPage } from '@/pages/ForgotPasswordPage'
import { apiResponse, stubFetch, type RecordedRequest } from '@/test/fake-fetch'

/** The wording the service sends back, deliberately the same for any address. */
const SERVICE_CONFIRMATION =
  'If that email address has an account, a reset link is on its way. The link expires in 30 minutes.'

afterEach(() => {
  vi.unstubAllGlobals()
})

function renderPage(...responses: Array<Response | Error>): RecordedRequest[] {
  const requests = stubFetch(...responses)

  render(
    <MemoryRouter>
      <ForgotPasswordPage />
    </MemoryRouter>,
  )

  return requests
}

function submitEmail(email: string) {
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: email } })
  fireEvent.click(screen.getByRole('button', { name: 'Send reset link' }))
}

describe('ForgotPasswordPage', () => {
  it('asks the service to email a link to the address given', async () => {
    const requests = renderPage(apiResponse(202, { message: SERVICE_CONFIRMATION }))

    submitEmail('ada@example.com')

    await waitFor(() => expect(requests).toHaveLength(1))
    expect(requests[0].path).toBe('/api/auth/forgot-password')
    expect(requests[0].method).toBe('POST')
    expect(requests[0].body).toEqual({ email: 'ada@example.com' })
  })

  it('shows the service’s own wording rather than one of its own', async () => {
    // The sentence is the same whether or not the address has an account, and
    // rewording it here would risk saying more than the service means to.
    renderPage(apiResponse(202, { message: SERVICE_CONFIRMATION }))

    submitEmail('ada@example.com')

    expect(await screen.findByText(SERVICE_CONFIRMATION)).toBeInTheDocument()
    expect(screen.getByText('Check your email')).toBeInTheDocument()
  })

  it('says the same thing for an address with no account', async () => {
    // The service answers 202 either way, so this page cannot become a way to
    // find out who is registered.
    renderPage(apiResponse(202, { message: SERVICE_CONFIRMATION }))

    submitEmail('nobody@example.com')

    expect(await screen.findByText(SERVICE_CONFIRMATION)).toBeInTheDocument()
  })

  it('tells the reader how long the link lasts', () => {
    renderPage(apiResponse(202, { message: SERVICE_CONFIRMATION }))

    expect(screen.getByText('The link expires 30 minutes after you ask for it.')).toBeInTheDocument()
  })

  it('refuses a malformed address without calling the service', async () => {
    const requests = renderPage(apiResponse(202, { message: SERVICE_CONFIRMATION }))

    submitEmail('not-an-email')

    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument()
    expect(requests).toHaveLength(0)
  })

  it('reports a request that never reached the service', async () => {
    renderPage(new TypeError('Failed to fetch'))

    submitEmail('ada@example.com')

    expect(
      await screen.findByText('Could not send the reset link. Please try again.'),
    ).toBeInTheDocument()
    // And the form is still there to try again with.
    expect(screen.getByRole('button', { name: 'Send reset link' })).toBeInTheDocument()
  })
})
