import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from 'react-router-dom'

import { AuthLayout } from '@/components/AuthLayout'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { requestPasswordReset } from '@/lib/auth-api'
import { forgotPasswordSchema, type ForgotPasswordValues } from '@/lib/auth-schemas'
import { applyApiErrorToForm } from '@/lib/form-errors'

/**
 * Asks for a password reset link (US-04).
 *
 * Open to anyone — a user who has forgotten their password has no session to
 * reach a guarded page with.
 */
export function ForgotPasswordPage() {
  const [formError, setFormError] = useState<string | null>(null)
  const [confirmation, setConfirmation] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<ForgotPasswordValues>({
    resolver: zodResolver(forgotPasswordSchema),
    defaultValues: { email: '' },
  })

  async function onSubmit(values: ForgotPasswordValues) {
    setFormError(null)

    try {
      const { message } = await requestPasswordReset(values)
      // The service's own wording, not ours. It says the same thing whether or
      // not the address has an account, and repeating that here in different
      // words would only risk saying more than it means to.
      setConfirmation(message)
    } catch (error) {
      setFormError(
        applyApiErrorToForm(error, setError, 'Could not send the reset link. Please try again.'),
      )
    }
  }

  if (confirmation) {
    return (
      <AuthLayout>
        <Card>
          <CardHeader>
            <CardTitle>Check your email</CardTitle>
            <CardDescription>{confirmation}</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <p className="text-muted-foreground text-sm">
              Open the link in the email to choose a new password. Nothing has changed on your
              account until you do.
            </p>
            <Button render={<Link to="/login" />} className="w-full">
              Back to sign in
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
          <CardTitle>Reset your password</CardTitle>
          <CardDescription>
            Enter the email address on your account and we will send you a link.
          </CardDescription>
        </CardHeader>

        <CardContent>
          <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-5">
            <Field>
              <FieldLabel htmlFor="email">Email</FieldLabel>
              <Input
                id="email"
                type="email"
                autoComplete="email"
                aria-invalid={Boolean(errors.email)}
                {...register('email')}
              />
              <FieldError errors={[errors.email]} />
              <FieldDescription>The link expires 30 minutes after you ask for it.</FieldDescription>
            </Field>

            {formError && (
              <p role="alert" className="text-destructive text-sm">
                {formError}
              </p>
            )}

            <Button type="submit" disabled={isSubmitting} className="w-full">
              {isSubmitting ? 'Sending link…' : 'Send reset link'}
            </Button>

            <p className="text-muted-foreground text-center text-sm">
              Remembered it?{' '}
              <Link to="/login" className="text-foreground underline underline-offset-4">
                Sign in
              </Link>
            </p>
          </form>
        </CardContent>
      </Card>
    </AuthLayout>
  )
}
