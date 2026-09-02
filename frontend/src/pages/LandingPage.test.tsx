import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { LandingPage } from '@/pages/LandingPage'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'

/** Stand-ins for the real pages, so these tests are about `/` and nothing else. */
const HOME_CONTENT = 'The authenticated home'
const LOGIN_CONTENT = 'The login page'
const REGISTER_CONTENT = 'The register page'

/**
 * Stores a token shaped like the one the User Service issues, minus a real
 * signature — the app never verifies one and cannot, so this takes the same
 * path through `decodeToken` as a real token would.
 */
function signIn() {
  const claims = {
    sub: '6f9619ff-8b86-d011-b42d-00cf4fc964ff',
    name: 'Ada Perera',
    email: 'ada@example.com',
    role: 'Client',
    exp: Math.floor(Date.now() / 1000) + 3600,
  }

  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')

  localStorage.setItem(TOKEN_STORAGE_KEY, `header.${payload}.signature`)
}

/** Renders `/` the way `App` wires it, with stubs standing in for the routes it leads to. */
function renderRoot() {
  render(
    <MemoryRouter initialEntries={['/']}>
      <AuthProvider>
        <Routes>
          <Route path="/" element={<LandingPage />} />
          <Route path="/home" element={<p>{HOME_CONTENT}</p>} />
          <Route path="/login" element={<p>{LOGIN_CONTENT}</p>} />
          <Route path="/register" element={<p>{REGISTER_CONTENT}</p>} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )
}

describe('LandingPage', () => {
  it('shows a visitor with no session what BuildNexus is', () => {
    renderRoot()

    // The name, and a one-line description of what the product does.
    expect(screen.getAllByText(/BuildNexus/).length).toBeGreaterThan(0)
    expect(
      screen.getByText(/plan,\s+track, and deliver construction projects/i),
    ).toBeInTheDocument()
  })

  it('offers a visitor with no session the two ways in', () => {
    renderRoot()

    // Several of each around the page; every one has to lead where it says.
    const loginLinks = screen.getAllByRole('link', { name: /log in/i })
    expect(loginLinks.length).toBeGreaterThan(0)
    loginLinks.forEach((link) => expect(link).toHaveAttribute('href', '/login'))

    const registerLinks = screen.getAllByRole('link', {
      name: /register|get started|create your account/i,
    })
    expect(registerLinks.length).toBeGreaterThan(0)
    registerLinks.forEach((link) => expect(link).toHaveAttribute('href', '/register'))
  })

  it('sends a signed-in visitor on to the authenticated home', () => {
    signIn()

    renderRoot()

    expect(screen.getByText(HOME_CONTENT)).toBeInTheDocument()
    // The public page never rendered.
    expect(screen.queryByText(/BuildNexus/)).not.toBeInTheDocument()
  })
})
