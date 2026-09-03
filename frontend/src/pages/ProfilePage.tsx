import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { fetchProfile, updateProfile, type UserProfile } from '@/lib/auth-api'
import { profileSchema, type ProfileValues } from '@/lib/auth-schemas'
import { applyApiErrorToForm } from '@/lib/form-errors'
import { ROLE_LABELS } from '@/lib/roles'

/** The service stores an unset contact detail as null; an input holds a string. */
function toFormValues(profile: UserProfile): ProfileValues {
  return {
    fullName: profile.fullName,
    phoneNumber: profile.phoneNumber ?? '',
    contactAddress: profile.contactAddress ?? '',
  }
}

/**
 * View and edit your own account details (US-02).
 *
 * The details come from the service rather than from the access token: the
 * token carries no contact details, and its copy of the name stops being
 * current the moment the profile is saved.
 */
export function ProfilePage() {
  const { authFetch } = useAuth()

  const [profile, setProfile] = useState<UserProfile | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [isSaved, setIsSaved] = useState(false)

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting, isDirty },
  } = useForm<ProfileValues>({
    resolver: zodResolver(profileSchema),
    defaultValues: { fullName: '', phoneNumber: '', contactAddress: '' },
  })

  useEffect(() => {
    let cancelled = false

    fetchProfile(authFetch)
      .then((loaded) => {
        if (cancelled) {
          return
        }

        setProfile(loaded)
        // Fill the form from the server, so the inputs start out showing what
        // is actually stored rather than empty strings.
        reset(toFormValues(loaded))
      })
      .catch(() => {
        if (!cancelled) {
          setLoadError('Could not load your profile. Please try again.')
        }
      })

    // The effect can outlive the page if the user navigates away mid-request.
    return () => {
      cancelled = true
    }
  }, [authFetch, reset])

  async function onSubmit(values: ProfileValues) {
    setFormError(null)
    setIsSaved(false)

    try {
      const saved = await updateProfile(authFetch, values)

      setProfile(saved)
      // Reset from the response, not the submitted values: this is what was
      // stored, and it leaves the form clean so "Save changes" goes quiet again.
      reset(toFormValues(saved))
      setIsSaved(true)
    } catch (error) {
      setFormError(
        applyApiErrorToForm(error, setError, 'Could not save your changes. Please try again.'),
      )
    }
  }

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-xl flex-col justify-center gap-4 p-6">
      <Card>
        <CardHeader>
          <CardTitle>Your profile</CardTitle>
          <CardDescription>Keep your name and contact details up to date.</CardDescription>
        </CardHeader>

        <CardContent>
          {loadError && (
            <p role="alert" className="text-destructive text-sm">
              {loadError}
            </p>
          )}

          {!loadError && !profile && (
            <p className="text-muted-foreground text-sm">Loading your profile…</p>
          )}

          {profile && (
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
                <FieldLabel htmlFor="phoneNumber">Phone number</FieldLabel>
                <Input
                  id="phoneNumber"
                  type="tel"
                  autoComplete="tel"
                  aria-invalid={Boolean(errors.phoneNumber)}
                  {...register('phoneNumber')}
                />
                <FieldDescription>Optional. Leave empty to remove it.</FieldDescription>
                <FieldError errors={[errors.phoneNumber]} />
              </Field>

              <Field>
                <FieldLabel htmlFor="contactAddress">Contact address</FieldLabel>
                <Input
                  id="contactAddress"
                  autoComplete="street-address"
                  aria-invalid={Boolean(errors.contactAddress)}
                  {...register('contactAddress')}
                />
                <FieldDescription>Optional. Leave empty to remove it.</FieldDescription>
                <FieldError errors={[errors.contactAddress]} />
              </Field>

              {/* Email and role are read-only on purpose: the email is the login
                  identity and the role decides what you may do, so neither is
                  self-editable. The service rejects a payload carrying either. */}
              <Field>
                <FieldLabel htmlFor="email">Email</FieldLabel>
                <Input id="email" type="email" value={profile.email} disabled readOnly />
                <FieldDescription>
                  Email and role cannot be changed here. Ask an administrator to change them for
                  you.
                </FieldDescription>
              </Field>

              <Field>
                <FieldLabel htmlFor="role">Role</FieldLabel>
                <Input id="role" value={ROLE_LABELS[profile.role]} disabled readOnly />
              </Field>

              {formError && (
                <p role="alert" className="text-destructive text-sm">
                  {formError}
                </p>
              )}

              {isSaved && !isDirty && (
                <p role="status" className="text-muted-foreground text-sm">
                  Your profile has been saved.
                </p>
              )}

              <Button type="submit" disabled={isSubmitting || !isDirty} className="w-full">
                {isSubmitting ? 'Saving…' : 'Save changes'}
              </Button>

              <p className="text-muted-foreground text-center text-sm">
                <Link to="/" className="text-foreground underline underline-offset-4">
                  Back to home
                </Link>
              </p>
            </form>
          )}
        </CardContent>
      </Card>
    </main>
  )
}
