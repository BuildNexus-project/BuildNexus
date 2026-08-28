import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { ResetPasswordPage } from '@/pages/ResetPasswordPage'
import { apiResponse, stubFetch, type RecordedRequest } from '@/test/fake-fetch'

/** Stands in for the token the emailed link carries. */
const TOKEN = 'a-token-from-the-email'

const SERVICE_CONFIRMATION = 'Your password has been reset. Sign in with your new password.'

/** What the service answers for an unknown, expired or already-used link. */
const REFUSED_LINK = {
  title: 'Invalid reset link',
  detail: 'This password reset link is no longer valid. Request a new one and try again.',
}

afterEach(() => {
  vi.unstubAllGlobals()
})

/**
 * Renders the page as if the user had opened the emailed link.
 *
 * The route is declared rather than the component rendered bare, so the token
 * reaches the page the way it really does — through the URL's query string.
 */
function openLink(
  search: string,
  ...responses: Array<Response | Error>
): RecordedRequest[] {
  const requests = stubFetch(...responses)

  render(
    <MemoryRouter initialEntries={[`/reset-password${search}`]}>
      <Routes>
        <Route path="/reset-password" element={<ResetPasswordPage />} />
      </Routes>
    </MemoryRouter>,
  )

  return requests
}

function submitPasswords(newPassword: string, confirmPassword = newPassword) {
  fireEvent.change(screen.getByLabelText('New password'), { target: { value: newPassword } })
  fireEvent.change(screen.getByLabelText('Confirm new password'), {
    target: { value: confirmPassword },
  })
  fireEvent.click(screen.getByRole('button', { name: 'Save new password' }))
}

describe('ResetPasswordPage', () => {
  it('sends the token from the link with the new password', async () => {
    const requests = openLink(
      `?token=${TOKEN}`,
      apiResponse(200, { message: SERVICE_CONFIRMATION }),
    )

    submitPasswords('NewPassw0rd')

    await waitFor(() => expect(requests).toHaveLength(1))
    expect(requests[0].path).toBe('/api/auth/reset-password')
    expect(requests[0].method).toBe('POST')
    expect(requests[0].body).toEqual({ token: TOKEN, newPassword: 'NewPassw0rd' })
  })

  it('confirms that the old password has stopped working', async () => {
    openLink(`?token=${TOKEN}`, apiResponse(200, { message: SERVICE_CONFIRMATION }))

    submitPasswords('NewPassw0rd')

    expect(await screen.findByText('Password changed')).toBeInTheDocument()
    expect(screen.getByText(SERVICE_CONFIRMATION)).toBeInTheDocument()
    expect(
      screen.getByText('Your old password no longer works, and this link cannot be used again.'),
    ).toBeInTheDocument()
  })

  it('does not ask for a password when the URL carries no token', () => {
    // Someone opened the page directly, or a mail client cut the link in half.
    // There is nothing to redeem, so offering the form would only waste a
    // password on a request that cannot succeed.
    const requests = openLink('')

    expect(screen.getByText('This link no longer works')).toBeInTheDocument()
    expect(screen.getByText('This page needs the link from your reset email.')).toBeInTheDocument()
    expect(screen.queryByLabelText('New password')).not.toBeInTheDocument()
    expect(requests).toHaveLength(0)
  })

  it('offers a fresh link when the service refuses this one', async () => {
    // Expired, already used or never issued — the service answers all three the
    // same way, and none of them is something editing the form can fix.
    openLink(`?token=${TOKEN}`, apiResponse(400, REFUSED_LINK))

    submitPasswords('NewPassw0rd')

    expect(await screen.findByText('This link no longer works')).toBeInTheDocument()
    expect(screen.getByText(REFUSED_LINK.detail)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Request a new link' })).toHaveAttribute(
      'href',
      '/forgot-password',
    )
    expect(screen.queryByLabelText('New password')).not.toBeInTheDocument()
  })

  it('says the password is unchanged when the link is refused', async () => {
    openLink(`?token=${TOKEN}`, apiResponse(400, REFUSED_LINK))

    submitPasswords('NewPassw0rd')

    expect(
      await screen.findByText(
        'Your password has not been changed. Ask for a new link and it will be sent again.',
      ),
    ).toBeInTheDocument()
  })

  it('keeps the form when the service objects to the password itself', async () => {
    // A 400 that names a field is a correctable mistake, unlike a dead link, so
    // the form stays put with the message on the input that caused it.
    openLink(
      `?token=${TOKEN}`,
      apiResponse(400, {
        title: 'One or more validation errors occurred.',
        errors: { NewPassword: ['Password must contain at least one letter and one digit.'] },
      }),
    )

    submitPasswords('Passw0rdOk')

    expect(
      await screen.findByText('Password must contain at least one letter and one digit.'),
    ).toBeInTheDocument()
    expect(screen.queryByText('This link no longer works')).not.toBeInTheDocument()
    expect(screen.getByLabelText('New password')).toBeInTheDocument()
  })

  it('refuses a password weaker than sign-up allows without calling the service', async () => {
    const requests = openLink(`?token=${TOKEN}`, apiResponse(200, { message: SERVICE_CONFIRMATION }))

    submitPasswords('nodigits')

    expect(
      await screen.findByText('Password must contain at least one letter and one digit.'),
    ).toBeInTheDocument()
    expect(requests).toHaveLength(0)
  })

  it('refuses a confirmation that does not match', async () => {
    // A typo in a password nobody can see would otherwise lock the user out of
    // the account they are in the middle of recovering.
    const requests = openLink(`?token=${TOKEN}`, apiResponse(200, { message: SERVICE_CONFIRMATION }))

    submitPasswords('NewPassw0rd', 'NewPassw0rdd')

    expect(await screen.findByText('Both passwords must match.')).toBeInTheDocument()
    expect(requests).toHaveLength(0)
  })
})
