import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { RoleRoute } from '@/auth/RoleRoute'
import { ADMIN_ROLES, type Role } from '@/lib/roles'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'
const GATED_ROUTE = '/admin/users'

/** Stand-ins for the real pages, so these tests are about the guard and nothing else. */
const GATED_CONTENT = 'The Admin user directory'
const LOGIN_CONTENT = 'The login page'

/**
 * Stores a token shaped like the one the User Service issues, minus a real
 * signature.
 *
 * The app never verifies a signature and cannot — the service does that — so an
 * unsigned token takes exactly the same path through `decodeToken` as a real
 * one, and lets a test choose the role it wants without a running service.
 */
function signInAs(role: Role) {
  const claims = {
    sub: '6f9619ff-8b86-d011-b42d-00cf4fc964ff',
    name: 'Ada Perera',
    email: 'ada@example.com',
    role,
    // Seconds, and comfortably in the future: AuthProvider discards a token
    // that has already expired.
    exp: Math.floor(Date.now() / 1000) + 3600,
  }

  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')

  localStorage.setItem(TOKEN_STORAGE_KEY, `header.${payload}.signature`)
}

/** Renders an Admin-only route, entered directly as if the URL were typed. */
function renderGatedRoute() {
  render(
    <MemoryRouter initialEntries={[GATED_ROUTE]}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<p>{LOGIN_CONTENT}</p>} />
          <Route
            path={GATED_ROUTE}
            element={
              <RoleRoute allowedRoles={ADMIN_ROLES}>
                <p>{GATED_CONTENT}</p>
              </RoleRoute>
            }
          />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )
}

describe('RoleRoute', () => {
  it('lets a role on the list through to the page', () => {
    signInAs('Admin')

    renderGatedRoute()

    expect(screen.getByText(GATED_CONTENT)).toBeInTheDocument()
  })

  it('keeps a role that is not on the list out, and tells them why', () => {
    signInAs('Client')

    renderGatedRoute()

    // Kept out: the page behind the guard never rendered.
    expect(screen.queryByText(GATED_CONTENT)).not.toBeInTheDocument()

    // And told why, rather than being bounced somewhere that hides it. The
    // sentence is built from several nodes, so it is read off the whole body.
    expect(screen.getByText('You do not have access to this page')).toBeInTheDocument()
    expect(document.body).toHaveTextContent('It is open to Admin.')
    expect(document.body).toHaveTextContent('You are signed in as Client.')
  })

  it('sends a visitor with no token to /login', () => {
    // Nothing in storage: not signed in at all, which is a different question
    // from holding the wrong role and gets a different answer.
    renderGatedRoute()

    expect(screen.getByText(LOGIN_CONTENT)).toBeInTheDocument()
    expect(screen.queryByText(GATED_CONTENT)).not.toBeInTheDocument()
  })
})
