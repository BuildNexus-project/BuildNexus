import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { HomePage } from '@/pages/HomePage'
import { apiResponse, stubFetch, type RecordedRequest } from '@/test/fake-fetch'
import type { Role } from '@/lib/roles'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'
const CLIENT_ID = '6f9619ff-8b86-d011-b42d-00cf4fc964ff'

function project(status: string, index: number) {
  return {
    id: `b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a${String(index).padStart(2, '0')}`,
    clientId: CLIENT_ID,
    name: `Project ${index}`,
    location: 'Galle',
    status,
    createdAt: '2026-08-01T09:00:00',
    updatedAt: '2026-08-04T09:00:00',
  }
}

/** Two still in design, one being built, one finished. */
function projects() {
  return [
    project('Pending', 1),
    project('DesignApproved', 2),
    project('Construction', 3),
    project('Completed', 4),
  ]
}

/**
 * Stores a token shaped like the one the User Service issues, minus a real
 * signature — the app never verifies one and cannot.
 */
function signInAs(role: Role, fullName = 'Ada Perera') {
  const claims = {
    sub: CLIENT_ID,
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

function renderPage(
  role: Role,
  response: Response | Error,
  fullName?: string,
): RecordedRequest[] {
  signInAs(role, fullName)
  const requests = stubFetch(response)

  render(
    <MemoryRouter>
      <AuthProvider>
        <HomePage />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

/** The destination cards under "Where would you like to go?". */
function destinations(): string[] {
  const section = screen.getByRole('heading', { name: 'Where would you like to go?' })
    .parentElement as HTMLElement

  return within(section)
    .getAllByRole('link')
    .map((link) => within(link).getByRole('heading').textContent ?? '')
}

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('HomePage', () => {
  it('welcomes the user by their first name', () => {
    renderPage('Client', apiResponse(200, projects()), 'Ayesha Rahman')

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Welcome back, Ayesha.')
  })

  it('names the role’s workspace', () => {
    renderPage('Architect', apiResponse(200, []))

    expect(screen.getByText('Architect workspace')).toBeInTheDocument()
  })

  it.each<[Role, string, string]>([
    ['Client', 'New project', '/projects/new'],
    ['Architect', 'Projects', '/projects'],
    ['ProjectManager', 'Projects', '/projects'],
    ['Admin', 'Users', '/admin/users'],
  ])('leads a %s to %s', (role, label, to) => {
    renderPage(role, apiResponse(200, []))

    // The button in the welcome banner, first among the links.
    const banner = screen.getByRole('heading', { level: 1 }).closest('section') as HTMLElement
    expect(within(banner).getByRole('link', { name: new RegExp(label) })).toHaveAttribute('href', to)
  })

  it.each<[Role, string[]]>([
    ['Client', ['Projects', 'New project', 'Progress', 'My costs', 'Your profile']],
    ['Architect', ['Projects', 'Project team', 'Your profile']],
    ['ProjectManager', ['Projects', 'Project team', 'Your profile']],
    ['Admin', ['Projects', 'Users', 'Design report', 'Your profile']],
  ])('offers a %s their own pages and the profile', (role, expected) => {
    renderPage(role, apiResponse(200, []))

    expect(destinations()).toEqual(expected)
  })
})

describe('HomePage project summary', () => {
  it('asks the service for the projects the caller may see', async () => {
    const requests = renderPage('Client', apiResponse(200, projects()))

    await screen.findByText('Your projects at a glance')
    expect(requests).toHaveLength(1)
    expect(requests[0].path).toBe('/api/projects')
  })

  it('counts where the projects stand', async () => {
    renderPage('Client', apiResponse(200, projects()))

    await screen.findByText('Your projects at a glance')

    const tile = (label: string) => screen.getByText(label).closest('a') as HTMLElement
    expect(tile('Active projects')).toHaveTextContent('4')
    // Pending and Design Approved are both still design.
    expect(tile('In design')).toHaveTextContent('2')
    expect(tile('Under construction')).toHaveTextContent('1')
    expect(tile('Completed')).toHaveTextContent('1')
  })

  it('shows zeroes, not nothing, when there are no projects yet', async () => {
    renderPage('Client', apiResponse(200, []))

    await screen.findByText('Your projects at a glance')

    expect(screen.getByText('Active projects').closest('a')).toHaveTextContent('0')
  })

  it.each<[Role, string]>([
    ['Client', 'Your projects at a glance'],
    ['Architect', 'Your assigned projects at a glance'],
    ['ProjectManager', 'Your assigned projects at a glance'],
    ['Admin', 'Every project at a glance'],
  ])('heads it for a %s as “%s”', async (role, heading) => {
    renderPage(role, apiResponse(200, []))

    expect(await screen.findByText(heading)).toBeInTheDocument()
  })

  it('leaves the summary out, rather than showing it wrong, when the projects cannot be read', async () => {
    renderPage('Client', apiResponse(500, { title: 'Boom' }))

    // The rest of the page is unaffected: the summary is a convenience, so a
    // failed read must neither show an error nor leave a page of dashes.
    expect(await screen.findByText('Where would you like to go?')).toBeInTheDocument()
    expect(screen.queryByText('Your projects at a glance')).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('does not show a summary before the projects have arrived', () => {
    renderPage('Client', apiResponse(200, projects()))

    // Synchronously after render, the request has not been answered.
    expect(screen.queryByText('Your projects at a glance')).not.toBeInTheDocument()
  })
})
