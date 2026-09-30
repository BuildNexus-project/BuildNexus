import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { StatTile } from '@/components/dashboard/StatTile'

/**
 * One figure on a dashboard. What is worth pinning is what looking at the page found: a long
 * amount ran out of its tile and, on a phone, pushed the whole page wider than the screen.
 */
function renderTile(value: React.ReactNode, to?: string) {
  render(
    <MemoryRouter>
      <StatTile label="Payments due" value={value} to={to} />
    </MemoryRouter>,
  )

  return screen.getByText(String(value))
}

describe('StatTile', () => {
  it('shows the label and the figure together', () => {
    renderTile(5)

    expect(screen.getByText('Payments due')).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()
  })

  it('sets a count or a percentage at full size', () => {
    expect(renderTile(1637)).toHaveClass('text-4xl')
  })

  it('keeps a short figure given as text at full size too', () => {
    expect(renderTile('14%')).toHaveClass('text-4xl')
  })

  it('sets a long amount smaller so it fits its tile instead of running out of it', () => {
    const figure = renderTile('LKR 4,810,000.00')

    // Smaller than a count everywhere, and smaller again on a phone.
    expect(figure).toHaveClass('text-base', 'sm:text-2xl')
    expect(figure).not.toHaveClass('text-4xl')
  })

  it('lets a long figure wrap rather than overflow', () => {
    expect(renderTile('LKR 4,810,000.00')).toHaveClass('break-words')
  })

  it('turns the formatter’s non-breaking space into an ordinary one, so an amount breaks at the space and not inside the number', () => {
    const { container } = render(
      <MemoryRouter>
        <StatTile label="Payments due" value={'LKR 4,810,000.00'} />
      </MemoryRouter>,
    )

    const figure = container.querySelector('.tabular-nums') as HTMLElement

    expect(figure.textContent).toBe('LKR 4,810,000.00')
    expect(figure.textContent).not.toContain(' ')
  })

  it('may shrink inside a grid, which a grid item will not do unless told', () => {
    renderTile('LKR 4,810,000.00')

    expect(screen.getByText('Payments due').parentElement).toHaveClass('min-w-0')
  })

  it('is a real link when it leads somewhere, and a plain readout when it does not', () => {
    const { unmount } = render(
      <MemoryRouter>
        <StatTile label="Users" value={3} to="/admin/users" />
      </MemoryRouter>,
    )
    expect(screen.getByRole('link')).toHaveAttribute('href', '/admin/users')
    unmount()

    render(
      <MemoryRouter>
        <StatTile label="Reports available" value={3} />
      </MemoryRouter>,
    )
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })
})
