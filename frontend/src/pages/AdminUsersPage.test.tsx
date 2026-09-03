import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent, { type UserEvent } from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { AdminUsersPage } from '@/pages/AdminUsersPage'
import { apiResponse, stubFetch, type RecordedRequest } from '@/test/fake-fetch'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'

/** The signed-in administrator, so the page can recognise its own row. */
const ADMIN_ID = '1f9619ff-8b86-d011-b42d-00cf4fc964ff'
const ARCHITECT_ID = '6f9619ff-8b86-d011-b42d-00cf4fc964ff'
const CLIENT_ID = 'c3e5a7b9-2d4f-4e6a-9b0c-1d2e3f4a5b6c'

function architect(overrides: Record<string, unknown> = {}) {
  return {
    id: ARCHITECT_ID,
    fullName: 'Nimal Fernando',
    email: 'nimal@example.com',
    role: 'Architect',
    isActive: true,
    createdAt: '2026-01-15T09:00:00',
    ...overrides,
  }
}

function client(overrides: Record<string, unknown> = {}) {
  return {
    id: CLIENT_ID,
    fullName: 'Ada Perera',
    email: 'ada@example.com',
    role: 'Client',
    isActive: true,
    createdAt: '2026-02-01T09:00:00',
    ...overrides,
  }
}

function admin(overrides: Record<string, unknown> = {}) {
  return {
    id: ADMIN_ID,
    fullName: 'Site Admin',
    email: 'admin@buildnexus.local',
    role: 'Admin',
    isActive: true,
    createdAt: '2026-01-01T09:00:00',
    ...overrides,
  }
}

/** One page of the directory, shaped as the service's envelope. */
function page(items: unknown[], overrides: Record<string, unknown> = {}) {
  return {
    items,
    page: 1,
    pageSize: 10,
    totalCount: items.length,
    totalPages: 1,
    ...overrides,
  }
}

/**
 * Stores a token shaped like the one the User Service issues, minus a real
 * signature — the app never verifies one and cannot.
 */
function signInAsAdmin() {
  const claims = {
    sub: ADMIN_ID,
    name: 'Site Admin',
    email: 'admin@buildnexus.local',
    role: 'Admin',
    exp: Math.floor(Date.now() / 1000) + 3600,
  }

  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')

  localStorage.setItem(TOKEN_STORAGE_KEY, `header.${payload}.signature`)
}

/**
 * Set by {@link renderPage}, for the interactions `fireEvent` cannot express.
 *
 * A Base UI select ignores a bare click on an option — it distinguishes a real
 * mouse press from a synthetic one — so choosing a role needs the full pointer
 * sequence user-event dispatches. Everything else stays on `fireEvent`, as the
 * other page tests do.
 */
let user: UserEvent

function renderPage(...responses: Array<Response | Error>): RecordedRequest[] {
  signInAsAdmin()
  user = userEvent.setup()
  const requests = stubFetch(...responses)

  render(
    <MemoryRouter>
      <AuthProvider>
        <AdminUsersPage />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

/** The row for one account, found by the name in it. */
async function rowFor(name: string) {
  return (await screen.findByText(name)).closest('tr') as HTMLElement
}

/**
 * Picks an option from a Base UI select.
 *
 * The popup is portalled and only mounted while the select is open, so the
 * trigger has to be clicked before the options exist to be found.
 */
async function choose(triggerName: string, optionName: string) {
  await user.click(screen.getByRole('combobox', { name: triggerName }))

  await user.click(await screen.findByRole('option', { name: optionName }))
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('AdminUsersPage', () => {
  describe('listing and paging', () => {
    it('asks for the first page of every role', async () => {
      const requests = renderPage(apiResponse(200, page([architect(), client()])))

      await screen.findByText('Nimal Fernando')

      expect(requests).toHaveLength(1)
      // No role on the query: "every role" is the absence of a filter, not a
      // filter naming all four.
      expect(requests[0].path).toBe('/api/users?page=1&pageSize=10')
    })

    it('lists each account with its role, status and registration date', async () => {
      renderPage(apiResponse(200, page([architect(), client()])))

      const row = await rowFor('Nimal Fernando')

      expect(within(row).getByText('nimal@example.com')).toBeInTheDocument()
      expect(within(row).getByText('Architect')).toBeInTheDocument()
      expect(within(row).getByText('Active')).toBeInTheDocument()
    })

    it('shows a deactivated account as deactivated rather than hiding it', async () => {
      // The one view that has to show them: reinstating an account starts with
      // finding it.
      renderPage(apiResponse(200, page([architect({ isActive: false })])))

      expect(within(await rowFor('Nimal Fernando')).getByText('Deactivated')).toBeInTheDocument()
    })

    it('reads ProjectManager as "Project Manager"', async () => {
      renderPage(apiResponse(200, page([architect({ role: 'ProjectManager' })])))

      expect(within(await rowFor('Nimal Fernando')).getByText('Project Manager')).toBeInTheDocument()
    })

    it('reports how many accounts there are across every page', async () => {
      renderPage(apiResponse(200, page([architect()], { totalCount: 34, totalPages: 4 })))

      expect(await screen.findByText('34 accounts')).toBeInTheDocument()
      expect(screen.getByText('Page 1 of 4')).toBeInTheDocument()
    })

    it('asks for the next page when Next is clicked', async () => {
      const requests = renderPage(
        apiResponse(200, page([architect()], { totalCount: 34, totalPages: 4 })),
        apiResponse(200, page([client()], { page: 2, totalCount: 34, totalPages: 4 })),
      )

      fireEvent.click(await screen.findByRole('button', { name: 'Next' }))

      await waitFor(() => expect(requests).toHaveLength(2))
      expect(requests[1].path).toBe('/api/users?page=2&pageSize=10')
      expect(await screen.findByText('Ada Perera')).toBeInTheDocument()
    })

    it('cannot go back from the first page or past the last', async () => {
      renderPage(apiResponse(200, page([architect()], { totalCount: 5, totalPages: 1 })))

      expect(await screen.findByRole('button', { name: 'Previous' })).toBeDisabled()
      expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
    })

    it('shows the reason the service gave when the directory cannot be loaded', async () => {
      renderPage(
        apiResponse(403, {
          title: 'Forbidden',
          detail: 'Your role does not permit this action.',
        }),
      )

      expect(await screen.findByText('Your role does not permit this action.')).toBeInTheDocument()
    })

    it('falls back to a plain message when the request fails with no reason', async () => {
      renderPage(new Error('offline'))

      await waitFor(() =>
        expect(
          screen.getByText('Could not load the user directory. Please try again.'),
        ).toBeInTheDocument(),
      )
    })
  })

  describe('filtering by role', () => {
    it('narrows the directory to the chosen role', async () => {
      const requests = renderPage(
        apiResponse(200, page([architect(), client()])),
        apiResponse(200, page([architect()])),
      )

      await screen.findByText('Nimal Fernando')
      await choose('Filter by role', 'Architect')

      await waitFor(() => expect(requests).toHaveLength(2))
      expect(requests[1].path).toBe('/api/users?role=Architect&page=1&pageSize=10')
    })

    it('returns to the first page when the filter changes', async () => {
      // A narrower list has fewer pages; staying on page 3 of the old one would
      // show an empty table.
      const requests = renderPage(
        apiResponse(200, page([architect()], { totalCount: 34, totalPages: 4 })),
        apiResponse(200, page([client()], { page: 2, totalCount: 34, totalPages: 4 })),
        apiResponse(200, page([client()])),
      )

      fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
      await waitFor(() => expect(requests).toHaveLength(2))

      await choose('Filter by role', 'Client')

      await waitFor(() => expect(requests).toHaveLength(3))
      expect(requests[2].path).toBe('/api/users?role=Client&page=1&pageSize=10')
    })

    it('says so when no account holds the chosen role', async () => {
      renderPage(
        apiResponse(200, page([architect()])),
        apiResponse(200, page([], { totalCount: 0 })),
      )

      await screen.findByText('Nimal Fernando')
      await choose('Filter by role', 'Project Manager')

      expect(
        await screen.findByText('No account holds the Project Manager role.'),
      ).toBeInTheDocument()
    })
  })

  describe('editing an account', () => {
    it('opens the dialog on the row the administrator picked', async () => {
      renderPage(apiResponse(200, page([architect()])))

      fireEvent.click(within(await rowFor('Nimal Fernando')).getByRole('button', { name: 'Edit' }))

      expect(await screen.findByRole('heading', { name: 'Edit account' })).toBeInTheDocument()
      expect(screen.getByLabelText('Full name')).toHaveValue('Nimal Fernando')
      expect(screen.getByLabelText('Email')).toHaveValue('nimal@example.com')
    })

    it('sends the new name, email and role, and shows the saved row', async () => {
      const requests = renderPage(
        apiResponse(200, page([architect()])),
        apiResponse(
          200,
          architect({ fullName: 'Nimal J. Fernando', email: 'nimal.j@example.com' }),
        ),
      )

      fireEvent.click(within(await rowFor('Nimal Fernando')).getByRole('button', { name: 'Edit' }))
      fireEvent.change(await screen.findByLabelText('Full name'), {
        target: { value: 'Nimal J. Fernando' },
      })
      fireEvent.change(screen.getByLabelText('Email'), {
        target: { value: 'nimal.j@example.com' },
      })
      fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))

      await waitFor(() => expect(requests).toHaveLength(2))
      expect(requests[1].path).toBe(`/api/users/${ARCHITECT_ID}`)
      expect(requests[1].method).toBe('PUT')
      // All three every time: the service replaces rather than patches.
      expect(requests[1].body).toEqual({
        fullName: 'Nimal J. Fernando',
        email: 'nimal.j@example.com',
        role: 'Architect',
      })

      // The row is replaced in place rather than the page being re-read.
      expect(await screen.findByText('Nimal J. Fernando')).toBeInTheDocument()
      expect(screen.queryByRole('heading', { name: 'Edit account' })).not.toBeInTheDocument()
    })

    it('changes a role from the dialog', async () => {
      const requests = renderPage(
        apiResponse(200, page([architect()])),
        apiResponse(200, architect({ role: 'ProjectManager' })),
      )

      fireEvent.click(within(await rowFor('Nimal Fernando')).getByRole('button', { name: 'Edit' }))
      await screen.findByLabelText('Full name')
      await choose('Role', 'Project Manager')
      fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))

      await waitFor(() => expect(requests).toHaveLength(2))
      expect(requests[1].body).toMatchObject({ role: 'ProjectManager' })
    })

    it('shows the service’s reason when the email already belongs to someone', async () => {
      renderPage(
        apiResponse(200, page([architect()])),
        apiResponse(409, {
          title: 'Email already registered',
          detail: 'An account with this email address already exists.',
        }),
      )

      fireEvent.click(within(await rowFor('Nimal Fernando')).getByRole('button', { name: 'Edit' }))
      fireEvent.change(await screen.findByLabelText('Email'), {
        target: { value: 'taken@example.com' },
      })
      fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))

      expect(
        await screen.findByText('An account with this email address already exists.'),
      ).toBeInTheDocument()
      // Still open, so the administrator can correct the address rather than
      // starting again.
      expect(screen.getByRole('heading', { name: 'Edit account' })).toBeInTheDocument()
    })

    it('puts a field error from the service on the field that caused it', async () => {
      renderPage(
        apiResponse(200, page([architect()])),
        apiResponse(400, {
          title: 'One or more validation errors occurred.',
          errors: { FullName: ['Full name must be between 2 and 150 characters.'] },
        }),
      )

      fireEvent.click(within(await rowFor('Nimal Fernando')).getByRole('button', { name: 'Edit' }))
      fireEvent.change(await screen.findByLabelText('Full name'), { target: { value: 'Nim' } })
      fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))

      expect(
        await screen.findByText('Full name must be between 2 and 150 characters.'),
      ).toBeInTheDocument()
    })

    it('catches a bad email before it reaches the service', async () => {
      const requests = renderPage(apiResponse(200, page([architect()])))

      fireEvent.click(within(await rowFor('Nimal Fernando')).getByRole('button', { name: 'Edit' }))
      fireEvent.change(await screen.findByLabelText('Email'), { target: { value: 'not-an-email' } })
      fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))

      expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument()
      // Only the initial load; nothing was sent.
      expect(requests).toHaveLength(1)
    })

    it('locks the role when an administrator edits their own account', async () => {
      // The service refuses self-demotion — only an Admin can reach it, so it
      // is the one edit that could leave nobody able to undo it.
      renderPage(apiResponse(200, page([admin(), architect()])))

      fireEvent.click(within(await rowFor('Site Admin')).getByRole('button', { name: 'Edit' }))

      await screen.findByLabelText('Full name')
      expect(screen.getByRole('combobox', { name: 'Role' })).toBeDisabled()
      expect(
        screen.getByText('You cannot change your own role. Ask another administrator to do it.'),
      ).toBeInTheDocument()
    })
  })

  describe('deactivating and reinstating', () => {
    it('asks for confirmation before withdrawing access', async () => {
      const requests = renderPage(apiResponse(200, page([architect()])))

      fireEvent.click(
        within(await rowFor('Nimal Fernando')).getByRole('button', { name: 'Deactivate' }),
      )

      expect(
        await screen.findByRole('heading', { name: 'Deactivate this account?' }),
      ).toBeInTheDocument()
      // Nothing has been sent yet — only the initial load.
      expect(requests).toHaveLength(1)
    })

    it('withdraws access once the confirmation is answered', async () => {
      const requests = renderPage(
        apiResponse(200, page([architect()])),
        apiResponse(200, architect({ isActive: false })),
      )

      fireEvent.click(
        within(await rowFor('Nimal Fernando')).getByRole('button', { name: 'Deactivate' }),
      )

      const dialog = await screen.findByRole('dialog')
      fireEvent.click(within(dialog).getByRole('button', { name: 'Deactivate' }))

      await waitFor(() => expect(requests).toHaveLength(2))
      expect(requests[1].path).toBe(`/api/users/${ARCHITECT_ID}/status`)
      expect(requests[1].method).toBe('PATCH')
      expect(requests[1].body).toEqual({ isActive: false })

      expect(within(await rowFor('Nimal Fernando')).getByText('Deactivated')).toBeInTheDocument()
    })

    it('sends nothing when the confirmation is cancelled', async () => {
      const requests = renderPage(apiResponse(200, page([architect()])))

      fireEvent.click(
        within(await rowFor('Nimal Fernando')).getByRole('button', { name: 'Deactivate' }),
      )

      const dialog = await screen.findByRole('dialog')
      fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

      await waitFor(() =>
        expect(
          screen.queryByRole('heading', { name: 'Deactivate this account?' }),
        ).not.toBeInTheDocument(),
      )
      expect(requests).toHaveLength(1)
    })

    it('reinstates a deactivated account without a confirmation', async () => {
      // Giving access back is not the destructive direction.
      const requests = renderPage(
        apiResponse(200, page([architect({ isActive: false })])),
        apiResponse(200, architect()),
      )

      fireEvent.click(
        within(await rowFor('Nimal Fernando')).getByRole('button', { name: 'Reinstate' }),
      )

      await waitFor(() => expect(requests).toHaveLength(2))
      expect(requests[1].body).toEqual({ isActive: true })
      expect(within(await rowFor('Nimal Fernando')).getByText('Active')).toBeInTheDocument()
    })

    it('does not offer an administrator the chance to deactivate themselves', async () => {
      // The service refuses it — that refusal is what keeps an active
      // administrator on the platform — so the button is not live either.
      renderPage(apiResponse(200, page([admin(), architect()])))

      expect(
        within(await rowFor('Site Admin')).getByRole('button', { name: 'Deactivate' }),
      ).toBeDisabled()
      expect(
        within(await rowFor('Nimal Fernando')).getByRole('button', { name: 'Deactivate' }),
      ).toBeEnabled()
    })

    it('shows the reason when the service refuses the change', async () => {
      renderPage(
        apiResponse(200, page([architect()])),
        apiResponse(400, {
          title: 'Bad Request',
          detail: 'You cannot deactivate your own account.',
        }),
      )

      fireEvent.click(
        within(await rowFor('Nimal Fernando')).getByRole('button', { name: 'Deactivate' }),
      )

      const dialog = await screen.findByRole('dialog')
      fireEvent.click(within(dialog).getByRole('button', { name: 'Deactivate' }))

      expect(
        await screen.findByText('You cannot deactivate your own account.'),
      ).toBeInTheDocument()
    })
  })
})
