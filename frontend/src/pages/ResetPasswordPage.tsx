import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useSearchParams } from 'react-router-dom'

import { AuthLayout } from '@/components/AuthLayout'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { ApiError } from '@/lib/api'
import { resetPassword } from '@/lib/auth-api'
import { resetPasswordSchema, type ResetPasswordValues } from '@/lib/auth-schemas'
import { applyApiErrorToForm } from '@/lib/form-errors'

/**
 * Shown whenever the link cannot work: no token in the URL, or a token the
 * service refused because it is unknown, expired or already used. The service
 * answers all three the same way, so there is one card for all of them.
 */
function DeadLinkCard({ reason }: { reason: string }) {
  return (
    <AuthLayout>
      <Card>
        <CardHeader>
          <CardTitle>This link no longer works</CardTitle>
          <CardDescription>{reason}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <p className="text-muted-foreground text-sm">
            Your password has not been changed. Ask for a new link and it will be sent again.
          </p>
          <Button render={<Link to="/forgot-password" />} className="w-full">
            Request a new link
          </Button>
        </CardContent>
      </Card>
    </AuthLayout>
  )
}

/**
 * Sets a new password from an emailed reset link (US-04).
 *
 * The token comes from the URL the email carried, never from something typed.
 * Completing this retires the old password: the service overwrites the stored
 * hash, so the previous one stops working from that moment.
 */
export function ResetPasswordPage() {
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token') ?? ''

  const [formError, setFormError] = useState<string | null>(null)
  const [linkRefused, setLinkRefused] = useState<string | null>(null)
  const [confirmation, setConfirmation] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<ResetPasswordValues>({
    resolver: zodResolver(resetPasswordSchema),
    defaultValues: { newPassword: '', confirmPassword: '' },
  })

  async function onSubmit(values: ResetPasswordValues) {
    setFormError(null)

    try {
      const { message } = await resetPassword({ token, newPassword: values.newPassword })
      setConfirmation(message)
    } catch (error) {
      // A 400 naming no field is the service refusing the link itself. That is
      // not something the form can be corrected into fixing, so it replaces the
      // form rather than appearing above it.
      if (
        error instanceof ApiError &&
        error.status === 400 &&
        Object.keys(error.fieldErrors).length === 0
      ) {
        setLinkRefused(error.detail ?? 'This password reset link is no longer valid.')
        return
      }

      setFormError(
        applyApiErrorToForm(error, setError, 'Could not reset your password. Please try again.'),
      )
    }
  }

  // Someone opened /reset-password directly, or the link was cut in half by a
  // mail client. There is nothing to redeem, so do not ask for a password.
  if (!token) {
    return <DeadLinkCard reason="This page needs the link from your reset email." />
  }

  if (linkRefused) {
    return <DeadLinkCard reason={linkRefused} />
  }

  if (confirmation) {
    return (
      <AuthLayout>
        <Card>
          <CardHeader>
            <CardTitle>Password changed</CardTitle>
            <CardDescription>{confirmation}</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <p className="text-muted-foreground text-sm">
              Your old password no longer works, and this link cannot be used again.
            </p>
            <Button render={<Link to="/login" />} className="w-full">
              Go to sign in
            </Button>
          </CardContent>
        </Card>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout>
      <Card>
        <CardHeader>
          <CardTitle>Choose a new password</CardTitle>
          <CardDescription>
            Pick something you have not used here before. Your old password stops working as soon
            as you save.
          </CardDescription>
        </CardHeader>

        <CardContent>
          <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-5">
            <Field>
              <FieldLabel htmlFor="newPassword">New password</FieldLabel>
              <Input
                id="newPassword"
                type="password"
                autoComplete="new-password"
                aria-invalid={Boolean(errors.newPassword)}
                {...register('newPassword')}
              />
              <FieldError errors={[errors.newPassword]} />
              <FieldDescription>
                At least 8 characters, with a letter and a digit.
              </FieldDescription>
            </Field>

            <Field>
              <FieldLabel htmlFor="confirmPassword">Confirm new password</FieldLabel>
              <Input
                id="confirmPassword"
                type="password"
                autoComplete="new-password"
                aria-invalid={Boolean(errors.confirmPassword)}
                {...register('confirmPassword')}
              />
              <FieldError errors={[errors.confirmPassword]} />
            </Field>

            {formError && (
              <p role="alert" className="text-destructive text-sm">
                {formError}
              </p>
            )}

            <Button type="submit" disabled={isSubmitting} className="w-full">
              {isSubmitting ? 'Saving…' : 'Save new password'}
            </Button>
          </form>
        </CardContent>
      </Card>
    </AuthLayout>
  )
}
