import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { RegisterPage } from '@/pages/RegisterPage'
import { apiResponse, stubFetch } from '@/test/fake-fetch'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('RegisterPage', () => {
  it.each(['771234567', '712345678'])('creates an account using nine digits (%s) after the fixed country code', async (digits) => {
    const values = {
      fullName: 'Ada Perera',
      email: 'ada@example.com',
      phoneNumber: `+94${digits}`,
      password: 'Passw0rd',
    }
    const requests = stubFetch(apiResponse(201, { ...values, role: 'Client' }))

    render(
      <MemoryRouter>
        <RegisterPage />
      </MemoryRouter>,
    )

    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Role')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Full name')).not.toBeInTheDocument()
    expect(screen.getByLabelText('First name')).toBeRequired()
    expect(screen.getByLabelText('Last name')).toBeRequired()
    fireEvent.change(screen.getByLabelText('First name'), { target: { value: ' Ada ' } })
    fireEvent.change(screen.getByLabelText('Last name'), { target: { value: ' Perera ' } })
    fireEvent.change(screen.getByLabelText('Email'), { target: { value: values.email } })
    expect(screen.getByLabelText('Phone number')).toBeRequired()
    expect(screen.getByLabelText('Phone number')).toHaveValue('')
    expect(screen.getByText('+94')).toBeInTheDocument()
    expect(screen.getByLabelText('Phone number')).not.toHaveAttribute('placeholder')
    await userEvent.setup().type(screen.getByLabelText('Phone number'), digits)
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: values.password } })
    fireEvent.change(screen.getByLabelText('Confirm password'), { target: { value: values.password } })
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }))

    expect(await screen.findByText('Account created')).toBeInTheDocument()
    expect(requests).toHaveLength(1)
    expect(requests[0]).toEqual({ path: '/api/auth/register', method: 'POST', body: values })
  })

  it.each(['', '123', '77123456', '077123456', '111234567'])('blocks account creation with missing or invalid national digits (%s)', async (phoneNumber) => {
    const requests = stubFetch()
    render(
      <MemoryRouter>
        <RegisterPage />
      </MemoryRouter>,
    )

    fireEvent.change(screen.getByLabelText('First name'), { target: { value: 'Ada' } })
    fireEvent.change(screen.getByLabelText('Last name'), { target: { value: 'Perera' } })
    fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'ada@example.com' } })
    fireEvent.change(screen.getByLabelText('Phone number'), { target: { value: phoneNumber } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'Passw0rd' } })
    fireEvent.change(screen.getByLabelText('Confirm password'), { target: { value: 'Passw0rd' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }))

    const message = phoneNumber.trim() ? 'Invalid mobile number' : 'Phone number is required.'
    expect(await screen.findByText(message)).toBeInTheDocument()
    expect(screen.getByLabelText('Phone number')).toHaveAttribute('aria-invalid', 'true')
    expect(requests).toHaveLength(0)
  })

  it('keeps the fixed country code when all entered digits are deleted', async () => {
    render(<MemoryRouter><RegisterPage /></MemoryRouter>)
    const user = userEvent.setup()
    const phone = screen.getByLabelText('Phone number')

    await user.type(phone, '7712345678')
    expect(phone).toHaveValue('771234567')
    await user.keyboard('{Control>}a{/Control}{Backspace}{Backspace}{Delete}')

    expect(phone).toHaveValue('')
    expect(screen.getByText('+94')).toBeInTheDocument()
  })

  it.each([
    ['', 'Confirm your password.'],
    ['Different1', 'Passwords do not match.'],
  ])('blocks signup until the password confirmation matches (%s)', async (confirmPassword, message) => {
    const requests = stubFetch(apiResponse(201, { fullName: 'Ada Perera', email: 'ada@example.com' }))
    render(<MemoryRouter><RegisterPage /></MemoryRouter>)

    fireEvent.change(screen.getByLabelText('First name'), { target: { value: 'Ada' } })
    fireEvent.change(screen.getByLabelText('Last name'), { target: { value: 'Perera' } })
    fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'ada@example.com' } })
    fireEvent.change(screen.getByLabelText('Phone number'), { target: { value: '771234567' } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'Passw0rd' } })
    fireEvent.change(screen.getByLabelText('Confirm password'), { target: { value: confirmPassword } })
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }))

    expect(await screen.findByText(message)).toBeInTheDocument()
    expect(requests).toHaveLength(0)
    expect(screen.getByLabelText('Confirm password')).toHaveAttribute('aria-invalid', 'true')

    fireEvent.change(screen.getByLabelText('Confirm password'), { target: { value: 'Passw0rd' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }))
    expect(await screen.findByText('Account created')).toBeInTheDocument()
    expect(requests).toHaveLength(1)
    expect(requests[0].body).toEqual({
      fullName: 'Ada Perera', email: 'ada@example.com', phoneNumber: '+94771234567', password: 'Passw0rd',
    })
  })

  it.each(['0771234567', '+94771234567'])('accepts pasting a complete number (%s) without duplicating the fixed prefix', async (phoneNumber) => {
    render(<MemoryRouter><RegisterPage /></MemoryRouter>)
    const user = userEvent.setup()
    const phone = screen.getByLabelText('Phone number')

    await user.click(phone)
    await user.paste(phoneNumber)

    expect(phone).toHaveValue('771234567')
    expect(screen.getByText('+94')).toBeInTheDocument()
  })
})
