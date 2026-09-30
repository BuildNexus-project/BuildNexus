import { describe, expect, it } from 'vitest'

import { generateInvoiceSchema, generateQuotationSchema } from './payment-schemas'

/**
 * The form edge for US-15's two money fields.
 *
 * These mirror the Payment Service's own `[Range]` and its `DECIMAL(15,2)`
 * columns. The service revalidates everything, so its answer is still the one
 * that counts — catching it here is what puts the message beside the field
 * instead of round-tripping a 400.
 */
describe('generateQuotationSchema', () => {
  it('accepts a positive total with two decimal places', () => {
    expect(generateQuotationSchema.safeParse({ estimatedTotal: 1_250_000.5 }).success).toBe(true)
    expect(generateQuotationSchema.safeParse({ estimatedTotal: 1_249_999.99 }).success).toBe(true)
    expect(generateQuotationSchema.safeParse({ estimatedTotal: 0.01 }).success).toBe(true)
  })

  it.each([0, -1, -0.01])('refuses %s, which is not an estimate', (estimatedTotal) => {
    const result = generateQuotationSchema.safeParse({ estimatedTotal })

    expect(result.success).toBe(false)
    expect(result.error?.issues[0]?.message).toContain('greater than zero')
  })

  it('refuses a third decimal place, which the column would silently round away', () => {
    // Not cosmetic: the column stores two decimals, so a third would make the
    // figure shown back differ from the figure typed.
    const result = generateQuotationSchema.safeParse({ estimatedTotal: 1.234 })

    expect(result.success).toBe(false)
    expect(result.error?.issues[0]?.message).toContain('two decimal places')
  })

  it("refuses a figure larger than the service's column holds", () => {
    // Caught here rather than as a 500 from MySQL.
    expect(generateQuotationSchema.safeParse({ estimatedTotal: 10_000_000_000_000 }).success).toBe(
      false,
    )
  })

  it('refuses a blank field, which arrives as NaN from a number input', () => {
    // `valueAsNumber` on an empty input yields NaN, not undefined — so the
    // finite check is what produces a message rather than a silent pass.
    expect(generateQuotationSchema.safeParse({ estimatedTotal: Number.NaN }).success).toBe(false)
  })
})

describe('generateInvoiceSchema', () => {
  it('accepts a positive amount', () => {
    expect(generateInvoiceSchema.safeParse({ amount: 250_000.75 }).success).toBe(true)
  })

  it.each([0, -5])('refuses %s', (amount) => {
    expect(generateInvoiceSchema.safeParse({ amount }).success).toBe(false)
  })

  it('has no status field, so a form cannot declare an invoice already paid', () => {
    // Mirrors the service's own request contract. An unknown key is stripped
    // rather than carried, so nothing reaches the API that it did not ask for.
    const result = generateInvoiceSchema.safeParse({ amount: 100, status: 'Paid' })

    expect(result.success).toBe(true)
    expect(result.data).toEqual({ amount: 100 })
  })
})
