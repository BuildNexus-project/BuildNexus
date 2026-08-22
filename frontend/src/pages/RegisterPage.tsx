import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from 'react-router-dom'

import { AuthLayout } from '@/components/AuthLayout'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { ApiError } from '@/lib/api'
import { registerUser } from '@/lib/auth-api'
import { registerSchema, type RegisterValues } from '@/lib/auth-schemas'
import { applyApiErrorToForm } from '@/lib/form-errors'
import { ROLE_LABELS, SELECTABLE_ROLES } from '@/lib/roles'

export function RegisterPage() {
  const [formError, setFormError] = useState<string | null>(null)
  const [registeredEmail, setRegisteredEmail] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    control,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<RegisterValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: { fullName: '', email: '', password: '' },
  })

  async function onSubmit(values: RegisterValues) {
    setFormError(null)

    try {
      await registerUser(values)
      setRegisteredEmail(values.email)
    } catch (error) {
      // A taken email is the one failure worth naming precisely, and it belongs
      // on the email input rather than in a general "registration failed".
      if (error instanceof ApiError && error.status === 409) {
        setError('email', {
          type: 'server',
          message: error.detail ?? 'An account with this email address already exists.',
        })
        return
      }

      setFormError(
        applyApiErrorToForm(error, setError, 'Could not create your account. Please try again.'),
      )
    }
  }

  if (registeredEmail) {
    return (
      <AuthLayout>
        <Card>
          <CardHeader>
            <CardTitle>Account created</CardTitle>
            <CardDescription>
              {registeredEmail} is ready to use. Sign in to continue.
            </CardDescription>
          </CardHeader>
          <CardContent>
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
          <CardTitle>Create your account</CardTitle>
          <CardDescription>Join BuildNexus to manage your construction projects.</CardDescription>
        </CardHeader>

        <CardContent>
          <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-5">
            <Field>
              <FieldLabel htmlFor="fullName">Full name</FieldLabel>
              <Input
                id="fullName"
                autoComplete="name"
                aria-invalid={Boolean(errors.fullName)}
                {...register('fullName')}
              />
              <FieldError errors={[errors.fullName]} />
            </Field>

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
            </Field>

            <Field>
              <FieldLabel htmlFor="password">Password</FieldLabel>
              <Input
                id="password"
                type="password"
                autoComplete="new-password"
                aria-invalid={Boolean(errors.password)}
                {...register('password')}
              />
              <FieldError errors={[errors.password]} />
            </Field>

            <Field>
              <FieldLabel htmlFor="role">Role</FieldLabel>
              <Controller
                control={control}
                name="role"
                render={({ field }) => (
                  <Select value={field.value ?? null} onValueChange={field.onChange}>
                    <SelectTrigger id="role" aria-invalid={Boolean(errors.role)} className="w-full">
                      <SelectValue placeholder="Select your role" />
                    </SelectTrigger>
                    <SelectContent>
                      {/* Admin is intentionally not offered — the service rejects it. */}
                      {SELECTABLE_ROLES.map((role) => (
                        <SelectItem key={role} value={role}>
                          {ROLE_LABELS[role]}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
              <FieldError errors={[errors.role]} />
            </Field>

            {formError && (
              <p role="alert" className="text-destructive text-sm">
                {formError}
              </p>
            )}

            <Button type="submit" disabled={isSubmitting} className="w-full">
              {isSubmitting ? 'Creating account…' : 'Create account'}
            </Button>

            <p className="text-muted-foreground text-center text-sm">
              Already have an account?{' '}
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
