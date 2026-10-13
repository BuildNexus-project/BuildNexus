import { useState } from 'react'
import { Eye, EyeOff } from 'lucide-react'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from 'react-router-dom'

import { AuthLayout } from '@/components/AuthLayout'
import { PhoneNumberInput } from '@/components/PhoneNumberInput'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { ApiError } from '@/lib/api'
import { registerUser } from '@/lib/auth-api'
import { registerSchema, type RegisterValues } from '@/lib/auth-schemas'
import { applyApiErrorToForm } from '@/lib/form-errors'

export function RegisterPage() {
  const [formError, setFormError] = useState<string | null>(null)
  const [registeredEmail, setRegisteredEmail] = useState<string | null>(null)
  const [showPassword, setShowPassword] = useState(false)
  const [showConfirmPassword, setShowConfirmPassword] = useState(false)

  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<RegisterValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: { firstName: '', lastName: '', email: '', phoneNumber: '', password: '', confirmPassword: '' },
  })

  async function onSubmit(values: RegisterValues) {
    setFormError(null)

    try {
      await registerUser({
        fullName: `${values.firstName} ${values.lastName}`,
        email: values.email,
        phoneNumber: values.phoneNumber,
        password: values.password,
      })
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
        applyApiErrorToForm(error, setError, 'Could not create your account. Please try again.', {
          fullName: 'lastName',
        }),
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
            <div className="grid gap-5 sm:grid-cols-2">
              <Field>
                <FieldLabel htmlFor="firstName">First name</FieldLabel>
                <Input
                  id="firstName"
                  autoComplete="given-name"
                  required
                  maxLength={150}
                  aria-invalid={Boolean(errors.firstName)}
                  {...register('firstName')}
                />
                <FieldError errors={[errors.firstName]} />
              </Field>

              <Field>
                <FieldLabel htmlFor="lastName">Last name</FieldLabel>
                <Input
                  id="lastName"
                  autoComplete="family-name"
                  required
                  maxLength={150}
                  aria-invalid={Boolean(errors.lastName)}
                  {...register('lastName')}
                />
                <FieldError errors={[errors.lastName]} />
              </Field>
            </div>

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
              <FieldLabel htmlFor="phoneNumber">Phone number</FieldLabel>
              <Controller
                control={control}
                name="phoneNumber"
                render={({ field }) => (
                  <PhoneNumberInput
                    name={field.name}
                    value={field.value}
                    onBlur={field.onBlur}
                    ref={field.ref}
                    id="phoneNumber"
                    required
                    aria-invalid={Boolean(errors.phoneNumber)}
                    onValueChange={field.onChange}
                  />
                )}
              />
              <FieldError errors={[errors.phoneNumber]} />
            </Field>

            <Field>
              <FieldLabel htmlFor="password">Password</FieldLabel>
              <div className="relative">
                <Input
                  id="password"
                  type={showPassword ? 'text' : 'password'}
                  autoComplete="new-password"
                  aria-invalid={Boolean(errors.password)}
                  className="pr-10"
                  {...register('password')}
                />
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="absolute inset-y-0 right-0 text-muted-foreground"
                  aria-label={showPassword ? 'Hide password' : 'Show password'}
                  aria-controls="password"
                  onClick={() => setShowPassword((visible) => !visible)}
                >
                  {showPassword ? <EyeOff aria-hidden="true" /> : <Eye aria-hidden="true" />}
                </Button>
              </div>
              <FieldError errors={[errors.password]} />
            </Field>

            <Field>
              <FieldLabel htmlFor="confirmPassword">Confirm password</FieldLabel>
              <div className="relative">
                <Input
                  id="confirmPassword"
                  type={showConfirmPassword ? 'text' : 'password'}
                  autoComplete="new-password"
                  required
                  aria-invalid={Boolean(errors.confirmPassword)}
                  className="pr-10"
                  {...register('confirmPassword')}
                />
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="absolute inset-y-0 right-0 text-muted-foreground"
                  aria-label={showConfirmPassword ? 'Hide confirm password' : 'Show confirm password'}
                  aria-controls="confirmPassword"
                  onClick={() => setShowConfirmPassword((visible) => !visible)}
                >
                  {showConfirmPassword ? <EyeOff aria-hidden="true" /> : <Eye aria-hidden="true" />}
                </Button>
              </div>
              <FieldError errors={[errors.confirmPassword]} />
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
