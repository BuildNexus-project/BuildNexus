import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useLocation, useNavigate } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { AuthLayout } from '@/components/AuthLayout'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { ApiError } from '@/lib/api'
import { loginUser } from '@/lib/auth-api'
import { loginSchema, type LoginValues } from '@/lib/auth-schemas'

const GENERIC_FAILURE = 'Invalid email or password.'

export function LoginPage() {
  const { signIn } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  // Where ProtectedRoute turned the visitor away from, if anywhere.
  const redirectTo = (location.state as { from?: { pathname?: string } } | null)?.from?.pathname ?? '/'
  const [formError, setFormError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
  })

  async function onSubmit(values: LoginValues) {
    setFormError(null)

    try {
      const { accessToken } = await loginUser(values)
      signIn(accessToken)
      navigate(redirectTo, { replace: true })
    } catch (error) {
      // Shown at form level, never against a single input: pointing at the email
      // or the password would reveal which half was wrong.
      if (error instanceof ApiError) {
        setFormError(error.status === 401 ? GENERIC_FAILURE : (error.detail ?? GENERIC_FAILURE))
        return
      }

      setFormError('Could not reach the server. Please try again.')
    }
  }

  return (
    <AuthLayout>
      <Card>
        <CardHeader>
          <CardTitle>Sign in</CardTitle>
          <CardDescription>Welcome back to BuildNexus.</CardDescription>
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
            </Field>

            <Field>
              <FieldLabel htmlFor="password">Password</FieldLabel>
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                aria-invalid={Boolean(errors.password)}
                {...register('password')}
              />
              <FieldError errors={[errors.password]} />
            </Field>

            {formError && (
              <p role="alert" className="text-destructive text-sm">
                {formError}
              </p>
            )}

            <Button type="submit" disabled={isSubmitting} className="w-full">
              {isSubmitting ? 'Signing in…' : 'Sign in'}
            </Button>

            <p className="text-muted-foreground text-center text-sm">
              Need an account?{' '}
              <Link to="/register" className="text-foreground underline underline-offset-4">
                Create one
              </Link>
            </p>
          </form>
        </CardContent>
      </Card>
    </AuthLayout>
  )
}
