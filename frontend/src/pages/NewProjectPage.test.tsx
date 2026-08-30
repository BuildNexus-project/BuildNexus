import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { NewProjectPage } from '@/pages/NewProjectPage'
import { apiResponse, stubFetch, type RecordedRequest } from '@/test/fake-fetch'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'
const CLIENT_ID = '6f9619ff-8b86-d011-b42d-00cf4fc964ff'

/** The answers a Client gives, and what the payload should carry for them. */
const FORM = {
  'Project name': 'Beachfront villa',
  Location: 'Galle',
  'Land size (perches)': '25.5',
  'Budget (LKR)': '18500000',
  Floors: '2',
  Bedrooms: '4',
  Bathrooms: '3',
  'Garage spaces': '2',
  'Other requirements': 'Solar hot water',
}

const PAYLOAD = {
  name: 'Beachfront villa',
  location: 'Galle',
  landSizePerches: 25.5,
  budget: 18500000,
  floors: 2,
  bedrooms: 4,
  bathrooms: 3,
  garageSpaces: 2,
  otherRequirements: 'Solar hot water',
}

/** The project as the service returns it, created Pending. */
function createdProject(overrides: Record<string, unknown> = {}) {
  return {
    id: 'b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b',
    clientId: CLIENT_ID,
    ...PAYLOAD,
    status: 'Pending',
    createdAt: '2026-08-30T09:15:00',
    ...overrides,
  }
}

/**
 * Stores a token shaped like the one the User Service issues, minus a real
 * signature — the app never verifies one and cannot, so this takes the same
 * path through `decodeToken` as a real token would.
 */
function signInAsClient() {
  const claims = {
    sub: CLIENT_ID,
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

function renderPage(...responses: Array<Response | Error>): RecordedRequest[] {
  signInAsClient()
  const requests = stubFetch(...responses)

  render(
    <MemoryRouter>
      <AuthProvider>
        <NewProjectPage />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

/** Fills the whole form, then applies any per-test changes on top. */
function fillForm(overrides: Record<string, string> = {}) {
  for (const [label, value] of Object.entries({ ...FORM, ...overrides })) {
    fireEvent.change(screen.getByLabelText(label), { target: { value } })
  }
}

function submit() {
  fireEvent.click(screen.getByRole('button', { name: 'Submit project' }))
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('NewProjectPage', () => {
  it('sends every requirement the form captured to the service', async () => {
    const requests = renderPage(apiResponse(201, createdProject()))

    fillForm()
    submit()

    await waitFor(() => expect(requests).toHaveLength(1))
    expect(requests[0].path).toBe('/api/projects')
    expect(requests[0].method).toBe('POST')
    expect(requests[0].body).toEqual(PAYLOAD)
  })

  it('sends the numbers as numbers rather than as the strings typed into them', async () => {
    // The service's contract is numeric; a payload of strings would come back
    // 400 on every field at once.
    const requests = renderPage(apiResponse(201, createdProject()))

    fillForm()
    submit()

    await waitFor(() => expect(requests).toHaveLength(1))

    const body = requests[0].body as Record<string, unknown>
    expect(typeof body.landSizePerches).toBe('number')
    expect(typeof body.budget).toBe('number')
    expect(typeof body.floors).toBe('number')
    expect(body.landSizePerches).toBe(25.5)
  })

  it('does not send the client id or a status', async () => {
    // Both are the service's to decide — it reads the client from the token and
    // creates the project Pending. Sending either would be asking for something
    // we are not allowed to choose.
    const requests = renderPage(apiResponse(201, createdProject()))

    fillForm()
    submit()

    await waitFor(() => expect(requests).toHaveLength(1))
    expect(requests[0].body).not.toHaveProperty('clientId')
    expect(requests[0].body).not.toHaveProperty('status')
  })

  it('accepts zero bedrooms, bathrooms and garage spaces', async () => {
    // Not every build is a house, and a garage is a count rather than a
    // checkbox, so zero is a real answer rather than a missing one.
    const requests = renderPage(apiResponse(201, createdProject()))

    fillForm({ Bedrooms: '0', Bathrooms: '0', 'Garage spaces': '0' })
    submit()

    await waitFor(() => expect(requests).toHaveLength(1))

    const body = requests[0].body as Record<string, unknown>
    expect(body.bedrooms).toBe(0)
    expect(body.bathrooms).toBe(0)
    expect(body.garageSpaces).toBe(0)
  })

  it('submits without other requirements, which is the one optional field', async () => {
    const requests = renderPage(apiResponse(201, createdProject({ otherRequirements: null })))

    fillForm({ 'Other requirements': '' })
    submit()

    await waitFor(() => expect(requests).toHaveLength(1))
    expect((requests[0].body as Record<string, unknown>).otherRequirements).toBe('')
  })

  it('refuses an empty count by name rather than sending it as zero', async () => {
    // The distinction the nullable fields exist for: "none" and "not answered"
    // are different, and only one of them may reach the service.
    const requests = renderPage(apiResponse(201, createdProject()))

    fillForm({ Bedrooms: '' })
    submit()

    expect(await screen.findByText('Number of bedrooms is required.')).toBeInTheDocument()
    expect(requests).toHaveLength(0)
  })

  it('refuses a building with no floors', async () => {
    const requests = renderPage(apiResponse(201, createdProject()))

    fillForm({ Floors: '0' })
    submit()

    expect(
      await screen.findByText('Number of floors must be between 1 and 100.'),
    ).toBeInTheDocument()
    expect(requests).toHaveLength(0)
  })

  it('refuses a budget of nothing', async () => {
    const requests = renderPage(apiResponse(201, createdProject()))

    fillForm({ 'Budget (LKR)': '0' })
    submit()

    expect(await screen.findByText('Budget must be greater than 0.')).toBeInTheDocument()
    expect(requests).toHaveLength(0)
  })

  it('trims the name and location before sending them', async () => {
    const requests = renderPage(apiResponse(201, createdProject()))

    fillForm({ 'Project name': '  Beachfront villa  ', Location: '  Galle  ' })
    submit()

    await waitFor(() => expect(requests).toHaveLength(1))

    const body = requests[0].body as Record<string, unknown>
    expect(body.name).toBe('Beachfront villa')
    expect(body.location).toBe('Galle')
  })

  it('confirms the project is Pending, using the status the service returned', async () => {
    // The AC the Client actually sees: their project was created and is waiting
    // to be picked up.
    renderPage(apiResponse(201, createdProject()))

    fillForm()
    submit()

    expect(await screen.findByText('Project submitted')).toBeInTheDocument()
    expect(document.body).toHaveTextContent('Beachfront villa is with us now.')
    expect(document.body).toHaveTextContent('Its status is Pending')
  })

  it('offers a blank form for a second project rather than the one just sent', async () => {
    renderPage(apiResponse(201, createdProject()))

    fillForm()
    submit()

    fireEvent.click(await screen.findByRole('button', { name: 'Submit another project' }))

    expect(screen.getByLabelText('Project name')).toHaveValue('')
    expect(screen.getByLabelText('Location')).toHaveValue('')
    expect(screen.getByLabelText('Other requirements')).toHaveValue('')
  })

  it('puts a message the service rejected a field with under that field', async () => {
    // Problem details name the property in PascalCase; it has to land on the
    // input that caused it rather than in a general "something went wrong".
    renderPage(
      apiResponse(400, {
        title: 'One or more validation errors occurred.',
        errors: { Name: ['A project with this name already exists for you.'] },
      }),
    )

    fillForm()
    submit()

    expect(
      await screen.findByText('A project with this name already exists for you.'),
    ).toBeInTheDocument()
  })

  it('shows a failure that names no field at form level', async () => {
    renderPage(
      apiResponse(403, {
        title: 'Not allowed for your role',
        detail: 'Your role does not permit this action.',
      }),
    )

    fillForm()
    submit()

    expect(await screen.findByText('Your role does not permit this action.')).toBeInTheDocument()
  })

  it('falls back to its own wording when the request fails with no detail at all', async () => {
    renderPage(new Error('Network is down'))

    fillForm()
    submit()

    expect(
      await screen.findByText('Could not submit your project. Please try again.'),
    ).toBeInTheDocument()
  })
})
