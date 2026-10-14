import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { ProfilePage } from '@/pages/ProfilePage'
import { apiResponse, stubFetch } from '@/test/fake-fetch'
import { signInAs, TEST_USER_ID } from '@/test/sign-in'

const PROFILE = {
  id: TEST_USER_ID, fullName: 'Ada Perera', email: 'ada@example.com',
  phoneNumber: '0771234567', contactAddress: 'Colombo', role: 'Client',
}

afterEach(() => vi.unstubAllGlobals())

function renderPage(...responses: Array<Response | Error>) {
  signInAs('Client')
  const requests = stubFetch(...responses)
  render(<MemoryRouter><AuthProvider><ProfilePage /></AuthProvider></MemoryRouter>)
  return requests
}

describe('ProfilePage', () => {
  it('displays the stored local number after the fixed prefix and saves edited digits as a complete number', async () => {
    const saved = { ...PROFILE, phoneNumber: '+94712345678' }
    const requests = renderPage(apiResponse(200, PROFILE), apiResponse(200, saved))
    const phone = await screen.findByLabelText('Phone number')
    expect(phone).toHaveValue('771234567')
    expect(screen.getByText('+94')).toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled()

    fireEvent.change(phone, { target: { value: '712345678' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    expect(await screen.findByRole('status')).toHaveTextContent('Your profile has been saved.')
    expect(requests[1]).toEqual({ path: '/api/users/me', method: 'PUT', body: { fullName: PROFILE.fullName, phoneNumber: saved.phoneNumber, contactAddress: PROFILE.contactAddress } })
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled()
  })

  it('allows a legacy account with no contact details to edit its name', async () => {
    const empty = { ...PROFILE, phoneNumber: null, contactAddress: null }
    const saved = { ...empty, fullName: 'Ada Silva' }
    const requests = renderPage(apiResponse(200, empty), apiResponse(200, saved))
    expect(await screen.findByLabelText('Phone number')).toHaveValue('')
    expect(screen.getByText('+94')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Full name'), { target: { value: saved.fullName } })
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    expect(await screen.findByRole('status')).toBeInTheDocument()
    expect(requests[1].body).toEqual({ fullName: saved.fullName, phoneNumber: '', contactAddress: '' })
  })

  it('shows a load failure without displaying an empty editable profile', async () => {
    renderPage(new TypeError('Failed to fetch'))
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load your profile. Please try again.')
    expect(screen.queryByLabelText('Phone number')).not.toBeInTheDocument()
  })

  it.each([
    [apiResponse(400, { errors: { PhoneNumber: ['Invalid mobile number'] } }), 'Invalid mobile number'],
    [new TypeError('Failed to fetch'), 'Could not save your changes. Please try again.'],
  ])('reports a rejected profile edit', async (response, message) => {
    renderPage(apiResponse(200, PROFILE), response as Response | Error)
    const phone = await screen.findByLabelText('Phone number')
    fireEvent.change(phone, { target: { value: '712345678' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    expect(await screen.findByText(message as string)).toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })
})
