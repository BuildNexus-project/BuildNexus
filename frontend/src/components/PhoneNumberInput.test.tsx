import { useState } from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { PhoneNumberInput } from '@/components/PhoneNumberInput'

function PhoneForm({ initialValue = '', onChange }: { initialValue?: string; onChange: (value: string) => void }) {
  const [value, setValue] = useState(initialValue)
  return <><label htmlFor="phone">Phone number</label><PhoneNumberInput id="phone" value={value} onValueChange={(next) => { setValue(next); onChange(next) }} /></>
}

describe('PhoneNumberInput', () => {
  it('ignores non-digit edits while preserving the existing number', () => {
    const onChange = vi.fn()
    render(<PhoneForm initialValue="+94771234567" onChange={onChange} />)
    const phone = screen.getByLabelText('Phone number')
    fireEvent.change(phone, { target: { value: '77123x567' } })
    expect(phone).toHaveValue('771234567')
    expect(onChange).not.toHaveBeenCalled()
  })

  it('accepts pasting nine national digits and submits the country code with them', async () => {
    const onChange = vi.fn()
    render(<PhoneForm onChange={onChange} />)
    const user = userEvent.setup()
    await user.click(screen.getByLabelText('Phone number'))
    await user.paste('771234567')
    expect(onChange).toHaveBeenCalledWith('+94771234567')
    expect(screen.getByLabelText('Phone number')).toHaveValue('771234567')
  })

  it.each(['not a number', '+44771234567', '7712345678'])('refuses an invalid paste (%s) without truncating it into a different number', async (pasted) => {
    const onChange = vi.fn()
    render(<PhoneForm onChange={onChange} />)
    const user = userEvent.setup()
    await user.click(screen.getByLabelText('Phone number'))
    await user.paste(pasted)
    expect(screen.getByLabelText('Phone number')).toHaveValue('')
    expect(onChange).not.toHaveBeenCalled()
    expect(screen.getByText('+94')).toBeInTheDocument()
  })
})
