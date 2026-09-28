import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { ProtectedRoute } from '@/auth/ProtectedRoute'
import type { Role } from '@/lib/roles'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'
const PAGE = 'The page itself'

/**
 * Stores a token shaped like the one the User Service issues, minus a real
 * signature — the app never verifies one and cannot, so this takes the same
 * path through `decodeToken` as a real token would.
 */
function signInAs(role: Role, fullName = 'Ada Perera') {
  const claims = {
    sub: '6f9619ff-8b86-d011-b42d-00cf4fc964ff',
    name: fullName,
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

/** Renders a protected page at `path`, so the frame around it is what is under test. */
function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<p>The login page</p>} />
          <Route
            path="*"
            element={
              <ProtectedRoute>
                <p>{PAGE}</p>
              </ProtectedRoute>
            }
          />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )
}

/** The links in the header's primary navigation, by their visible names. */
function primaryNavNames(): string[] {
  return within(screen.getByRole('navigation', { name: 'Primary' }))
    .getAllByRole('link')
    .map((link) => link.textContent ?? '')
}

afterEach(() => {
  localStorage.clear()
})

describe('AppShell', () => {
  it('puts the page between a header and a footer', () => {
    signInAs('Client')

    renderAt('/projects')

    expect(screen.getByRole('banner')).toBeInTheDocument()
    expect(screen.getByRole('contentinfo')).toBeInTheDocument()
    expect(screen.getByText(PAGE)).toBeInTheDocument()
  })

  it('links the brand back to the dashboard', () => {
    signInAs('Client')

    renderAt('/projects')

    expect(screen.getByRole('link', { name: 'BuildNexus home' })).toHaveAttribute('href', '/home')
  })

  it('offers a way past the navigation to the page', () => {
    signInAs('Client')

    renderAt('/projects')

    expect(screen.getByRole('link', { name: 'Skip to content' })).toHaveAttribute(
      'href',
      '#content',
    )
  })
})

describe('the header navigation', () => {
  it.each<[Role, string[]]>([
    ['Client', ['Dashboard', 'Projects', 'New project', 'Progress', 'My costs']],
    ['Architect', ['Dashboard', 'Projects', 'Project team']],
    ['ProjectManager', ['Dashboard', 'Projects', 'Project team']],
    ['Admin', ['Dashboard', 'Projects', 'Users', 'Design report']],
  ])('shows a %s their own pages', (role, expected) => {
    signInAs(role)

    renderAt('/projects')

    expect(primaryNavNames()).toEqual(expected)
  })

  it('does not invite a Client into the admin pages', () => {
    signInAs('Client')

    renderAt('/projects')

    expect(screen.queryByRole('link', { name: 'Users' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Design report' })).not.toBeInTheDocument()
  })

  it('marks the page the user is on', () => {
    signInAs('Client')

    renderAt('/projects')

    const nav = within(screen.getByRole('navigation', { name: 'Primary' }))
    expect(nav.getByRole('link', { name: 'Projects' })).toHaveAttribute('aria-current', 'page')
    expect(nav.getByRole('link', { name: 'Dashboard' })).not.toHaveAttribute('aria-current')
  })

  it('marks New project, and not Projects, on the new-project page', () => {
    signInAs('Client')

    renderAt('/projects/new')

    const nav = within(screen.getByRole('navigation', { name: 'Primary' }))
    expect(nav.getByRole('link', { name: 'New project' })).toHaveAttribute('aria-current', 'page')
    expect(nav.getByRole('link', { name: 'Projects' })).not.toHaveAttribute('aria-current')
  })

  it('shows who is signed in, with their initials and role', () => {
    signInAs('Architect', 'Nimali Fernando')

    renderAt('/projects')

    // The header's own link; the footer has another, tested with the footer.
    const profile = within(screen.getByRole('banner')).getByRole('link', { name: 'Your profile' })
    expect(profile).toHaveAttribute('href', '/profile')
    expect(profile).toHaveTextContent('NF')
    expect(profile).toHaveTextContent('Nimali Fernando')
    expect(profile).toHaveTextContent('Architect')
  })
})

describe('the breadcrumb strip', () => {
  it('leads from Home to the current page', () => {
    signInAs('Client')

    renderAt('/projects/new')

    const trail = within(screen.getByRole('navigation', { name: 'Breadcrumb' }))
    expect(trail.getByRole('link', { name: 'Home' })).toHaveAttribute('href', '/home')
    expect(trail.getByRole('link', { name: 'Projects' })).toHaveAttribute('href', '/projects')
    // The page itself is where the user is: named, but not a link.
    expect(trail.getByText('New project')).toHaveAttribute('aria-current', 'page')
    expect(trail.queryByRole('link', { name: 'New project' })).not.toBeInTheDocument()
  })

  it('names the role whose workspace this is', () => {
    signInAs('ProjectManager')

    renderAt('/projects')

    expect(screen.getByText('Project manager workspace')).toBeInTheDocument()
  })

  it('is left out on the dashboard, which is the top of the trail', () => {
    signInAs('Client')

    renderAt('/home')

    expect(screen.queryByRole('navigation', { name: 'Breadcrumb' })).not.toBeInTheDocument()
  })
})

describe('the footer', () => {
  it('repeats the role’s own navigation', () => {
    signInAs('Admin')

    renderAt('/projects')

    const footer = within(screen.getByRole('contentinfo'))
    const nav = within(footer.getByRole('navigation', { name: 'Footer' }))
    expect(nav.getAllByRole('link').map((link) => link.textContent)).toEqual([
      'Dashboard',
      'Projects',
      'Users',
      'Design report',
    ])
  })

  it('says who is signed in and offers the profile and Sign out', () => {
    signInAs('Client', 'Ayesha Rahman')

    renderAt('/projects')

    const footer = within(screen.getByRole('contentinfo'))
    expect(footer.getByText('Ayesha Rahman')).toBeInTheDocument()
    expect(footer.getByRole('link', { name: 'Your profile' })).toHaveAttribute('href', '/profile')
    expect(footer.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
  })
})

describe('signing out', () => {
  it('ends the session from the header and sends the user to sign in', async () => {
    signInAs('Client')
    renderAt('/projects')

    await userEvent.click(
      within(screen.getByRole('banner')).getByRole('button', { name: 'Sign out' }),
    )

    expect(localStorage.getItem(TOKEN_STORAGE_KEY)).toBeNull()
    expect(await screen.findByText('The login page')).toBeInTheDocument()
  })

  it('ends the session from the footer too', async () => {
    signInAs('Client')
    renderAt('/projects')

    await userEvent.click(
      within(screen.getByRole('contentinfo')).getByRole('button', { name: 'Sign out' }),
    )

    expect(localStorage.getItem(TOKEN_STORAGE_KEY)).toBeNull()
    expect(await screen.findByText('The login page')).toBeInTheDocument()
  })
})

describe('the mobile menu', () => {
  it('is closed until asked for, and opens and closes from the menu button', async () => {
    signInAs('Client')
    renderAt('/projects')

    const toggle = screen.getByRole('button', { name: 'Open menu' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('navigation', { name: 'Mobile' })).not.toBeInTheDocument()

    await userEvent.click(toggle)

    expect(screen.getByRole('button', { name: 'Close menu' })).toHaveAttribute(
      'aria-expanded',
      'true',
    )
    const mobile = within(screen.getByRole('navigation', { name: 'Mobile' }))
    expect(mobile.getByRole('link', { name: 'New project' })).toHaveAttribute(
      'href',
      '/projects/new',
    )

    await userEvent.click(screen.getByRole('button', { name: 'Close menu' }))

    expect(screen.queryByRole('navigation', { name: 'Mobile' })).not.toBeInTheDocument()
  })

  it('closes when the user follows a link in it', async () => {
    signInAs('Client')
    renderAt('/projects')

    await userEvent.click(screen.getByRole('button', { name: 'Open menu' }))
    await userEvent.click(
      within(screen.getByRole('navigation', { name: 'Mobile' })).getByRole('link', {
        name: 'Progress',
      }),
    )

    expect(screen.queryByRole('navigation', { name: 'Mobile' })).not.toBeInTheDocument()
  })
})
