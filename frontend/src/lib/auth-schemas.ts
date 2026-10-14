import { z } from 'zod'

import { ROLES } from './roles'

/**
 * The rules a password has to meet, wherever one is chosen — signing up or
 * resetting a forgotten one. Shared so the two can never drift apart and let a
 * reset set a password the sign-up form would have refused. The service applies
 * the same rules to both.
 */
const passwordSchema = z
  .string()
  .min(8, 'Password must be at least 8 characters.')
  .max(128, 'Password must not exceed 128 characters.')
  .regex(/(?=.*[A-Za-z])(?=.*\d)/, 'Password must contain at least one letter and one digit.')

const emailSchema = z
  .email('Enter a valid email address.')
  .max(255, 'Email must not exceed 255 characters.')

/** Phone number format, using the same rules for signup and profile edits. */
const phoneNumberSchema = z
  .string()
  .trim()
  .max(30, 'Phone number must not exceed 30 characters.')
  .regex(
    /^(?:07\d{8}|\+947\d{8})?$/,
    'Invalid mobile number',
  )

/**
 * Checks registration before sending it to the service. The two name fields
 * are combined into the full name stored by the service, with its 150-character limit.
 */
export const registerSchema = z.object({
  firstName: z
    .string()
    .trim()
    .min(1, 'First name is required.')
    .max(150, 'First name must not exceed 150 characters.'),
  lastName: z
    .string()
    .trim()
    .min(1, 'Last name is required.')
    .max(150, 'Last name must not exceed 150 characters.'),
  email: emailSchema,
  phoneNumber: phoneNumberSchema.min(1, 'Phone number is required.'),
  password: passwordSchema,
  confirmPassword: z.string().min(1, 'Confirm your password.'),
}).refine((values) => `${values.firstName} ${values.lastName}`.length <= 150, {
  message: 'First and last name together must not exceed 150 characters.',
  path: ['lastName'],
}).refine((values) => !values.confirmPassword || values.password === values.confirmPassword, {
  message: 'Passwords do not match.',
  path: ['confirmPassword'],
})

export type RegisterValues = z.infer<typeof registerSchema>

/**
 * Login deliberately checks presence only — no length or complexity rules.
 * Applying the registration rules here would let a caller tell a badly-formed
 * password apart from a wrong one, which is exactly what the service's single
 * generic error avoids.
 */
export const loginSchema = z.object({
  email: z.email('Enter a valid email address.'),
  password: z.string().min(1, 'Enter your password.'),
})

export type LoginValues = z.infer<typeof loginSchema>

/**
 * Starting a password reset asks for the address and nothing else — someone who
 * has forgotten their password has nothing else to offer.
 */
export const forgotPasswordSchema = z.object({
  email: emailSchema,
})

export type ForgotPasswordValues = z.infer<typeof forgotPasswordSchema>

/**
 * Choosing the replacement password. The token is not here: it arrives in the
 * URL rather than being typed, so there is nothing for the user to get wrong
 * about it and no input to validate.
 *
 * The confirmation field is this side only. The service has no use for it — it
 * exists so a typo in a password nobody can see does not lock the user out of
 * the account they are in the middle of recovering.
 */
export const resetPasswordSchema = z
  .object({
    newPassword: passwordSchema,
    confirmPassword: z.string().min(1, 'Re-enter your new password.'),
  })
  .refine((values) => values.newPassword === values.confirmPassword, {
    message: 'Both passwords must match.',
    path: ['confirmPassword'],
  })

export type ResetPasswordValues = z.infer<typeof resetPasswordSchema>

/**
 * Mirrors the User Service's own profile rules, so most mistakes are caught
 * before a request is made. The service revalidates everything.
 *
 * Email and role are absent by design: neither is self-editable, so the form
 * shows them read-only rather than collecting a value the service would refuse.
 */
export const profileSchema = z.object({
  fullName: z
    .string()
    .trim()
    .min(2, 'Full name must be at least 2 characters.')
    .max(150, 'Full name must not exceed 150 characters.'),
  /**
   * Blank clears the stored number. Otherwise require 07 and eight digits,
   * or +947 and eight digits, matching the service's signup and profile rules.
   */
  phoneNumber: phoneNumberSchema,
  /** Blank clears the stored address. */
  contactAddress: z.string().trim().max(255, 'Contact address must not exceed 255 characters.'),
})

export type ProfileValues = z.infer<typeof profileSchema>

/**
 * Mirrors the User Service's own rules for an administrator's edit of somebody
 * else's account, so most mistakes are caught before a request is made. The
 * service revalidates everything.
 *
 * The name rules are the profile form's, since it is the same column. Email and
 * role are here rather than read-only as they are on the profile form — that is
 * the whole difference between the two, and the reason the profile form tells
 * the user to ask an administrator.
 *
 * An administrator may grant any platform role. Self-service registration
 * always creates a Client account.
 */
export const adminUserSchema = z.object({
  fullName: z
    .string()
    .trim()
    .min(2, 'Full name must be at least 2 characters.')
    .max(150, 'Full name must not exceed 150 characters.'),
  email: emailSchema,
  role: z.enum(ROLES, 'Choose a role for this account.'),
})

export type AdminUserValues = z.infer<typeof adminUserSchema>
