import { z } from 'zod'

/**
 * The largest figure the service's `DECIMAL(15,2)` columns hold: thirteen
 * digits before the decimal point. Mirrored here so an over-large amount is
 * caught beside the field rather than as a round-tripped 400.
 */
const MAX_MONEY = 9_999_999_999_999.99

/**
 * Money as typed into a form: a positive amount with at most two decimal
 * places.
 *
 * The two-decimal rule is not cosmetic — the column stores two, so a third
 * would be silently rounded away and the figure shown back would not be the
 * figure entered. Refusing it at the edge keeps what the user typed and what
 * is stored the same thing.
 */
function money(label: string) {
  return z
    .number({ message: `${label} is required.` })
    .refine((value) => Number.isFinite(value), { message: `${label} must be a number.` })
    .refine((value) => value > 0, { message: `${label} must be greater than zero.` })
    .refine((value) => value <= MAX_MONEY, {
      message: `${label} must be no more than ${MAX_MONEY.toLocaleString()}.`,
    })
    .refine((value) => Math.round(value * 100) / 100 === value, {
      message: `${label} must have at most two decimal places.`,
    })
}

/**
 * The generate-quotation form: just the estimated total. The project id is in
 * the URL and the author comes from the caller's token — neither belongs in the
 * payload. Mirrors the Payment Service's own `GenerateQuotationRequest`
 * (`[Required]`, `[Range]`), so most mistakes are caught before a request is
 * made. The service revalidates everything, so its answer is still the one that
 * counts.
 */
export const generateQuotationSchema = z.object({
  estimatedTotal: money('An estimated total'),
})

export type GenerateQuotationValues = z.infer<typeof generateQuotationSchema>

/**
 * The generate-invoice form: just the amount.
 *
 * There is deliberately no status field, mirroring the service's own request
 * contract: a raised invoice is always `Pending`, and letting the form declare
 * one already paid would make the billing record a matter of who asked.
 */
export const generateInvoiceSchema = z.object({
  amount: money('An amount'),
})

export type GenerateInvoiceValues = z.infer<typeof generateInvoiceSchema>
