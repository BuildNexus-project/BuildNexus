import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthContext } from '@/auth/auth-context'
import { LoginPage } from '@/pages/LoginPage'
import { apiResponse, stubFetch } from '@/test/fake-fetch'

afterEach(() => vi.unstubAllGlobals())

function Destination() {
  return <p>Destination: {useLocation().pathname}</p>
}

function renderPage(responses: Array<Response | Error>, from?: string) {
  const requests = stubFetch(...responses)
  const signIn = vi.fn()
  render(
    <MemoryRouter initialEntries={[{ pathname: '/login', state: from ? { from: { pathname: from } } : null }]}>
      <AuthContext.Provider value={{ user: null, token: null, isAuthenticated: false, signIn, signOut: vi.fn(), authFetch: vi.fn() }}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="*" element={<Destination />} />
        </Routes>
      </AuthContext.Provider>
    </MemoryRouter>,
  )
  return { requests, signIn }
}

function submitLogin(email = 'ada@example.com', password = 'Passw0rd') {
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: email } })
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: password } })
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))
}

describe('LoginPage', () => {
  it.each(['/', '/projects'])('signs in and returns the user to %s', async (destination) => {
    const { requests, signIn } = renderPage([apiResponse(200, { accessToken: 'issued-token' })], destination === '/' ? undefined : destination)
    submitLogin()

    expect(await screen.findByText(`Destination: ${destination}`)).toBeInTheDocument()
    expect(signIn).toHaveBeenCalledWith('issued-token')
    expect(requests[0]).toEqual({ path: '/api/auth/login', method: 'POST', body: { email: 'ada@example.com', password: 'Passw0rd' } })
  })

  it('lets the user show and hide the password without submitting or changing it', () => {
    const { requests } = renderPage([])
    const password = screen.getByLabelText('Password')
    fireEvent.change(password, { target: { value: 'Passw0rd' } })
    expect(password).toHaveAttribute('type', 'password')

    fireEvent.click(screen.getByRole('button', { name: 'Show password' }))
    expect(password).toHaveAttribute('type', 'text')
    expect(password).toHaveValue('Passw0rd')
    fireEvent.click(screen.getByRole('button', { name: 'Hide password' }))
    expect(password).toHaveAttribute('type', 'password')
    expect(requests).toHaveLength(0)
  })

  it.each([
    [apiResponse(401, { detail: 'Do not expose the account lookup.' }), 'Invalid email or password.'],
    [apiResponse(503, { detail: 'Please try again later.' }), 'Please try again later.'],
    [apiResponse(503, {}), 'Invalid email or password.'],
    [new TypeError('Failed to fetch'), 'Could not reach the server. Please try again.'],
  ])('shows the appropriate sign-in failure', async (response, message) => {
    const { signIn } = renderPage([response as Response | Error])
    submitLogin()
    expect(await screen.findByRole('alert')).toHaveTextContent(message as string)
    expect(signIn).not.toHaveBeenCalled()
  })

  it('validates the email and password before calling the service', async () => {
    const { requests } = renderPage([])
    submitLogin('not-an-email', '')
    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument()
    expect(screen.getByText('Enter your password.')).toBeInTheDocument()
    expect(requests).toHaveLength(0)
  })
})
