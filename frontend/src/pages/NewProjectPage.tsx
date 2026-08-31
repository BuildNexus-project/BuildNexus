import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { createProject, type Project } from '@/lib/project-api'
import { newProjectSchema, type NewProjectValues } from '@/lib/project-schemas'
import { applyApiErrorToForm } from '@/lib/form-errors'

/** Empty rather than zero, so a Client fills each count in deliberately. */
const EMPTY_FORM = {
  name: '',
  location: '',
  otherRequirements: '',
}

/**
 * Submit a new construction project (US-05).
 *
 * Client only — the route guard keeps other roles out, and the endpoint behind
 * it refuses them with 403 whatever the browser does.
 */
export function NewProjectPage() {
  const { authFetch } = useAuth()

  const [submitted, setSubmitted] = useState<Project | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<NewProjectValues>({
    resolver: zodResolver(newProjectSchema),
    defaultValues: EMPTY_FORM,
  })

  async function onSubmit(values: NewProjectValues) {
    setFormError(null)

    try {
      setSubmitted(await createProject(authFetch, values))
    } catch (error) {
      setFormError(
        applyApiErrorToForm(error, setError, 'Could not submit your project. Please try again.'),
      )
    }
  }

  if (submitted) {
    return (
      <main className="mx-auto flex min-h-svh w-full max-w-xl flex-col justify-center gap-4 p-6">
        <Card>
          <CardHeader>
            <CardTitle>Project submitted</CardTitle>
            <CardDescription>
              {submitted.name} is with us now. Its status is {submitted.status} until the company
              picks it up — we will be in touch once someone has reviewed it.
            </CardDescription>
          </CardHeader>

          <CardContent className="flex flex-col gap-4">
            <Button
              variant="outline"
              onClick={() => {
                // Back to a blank form rather than the one just sent, so a
                // second project does not start as a copy of the first.
                reset(EMPTY_FORM)
                setSubmitted(null)
              }}
              className="w-full"
            >
              Submit another project
            </Button>

            {/* Straight into the project just created, so a Client can watch
                it from the moment they submit it rather than having to find it
                again. */}
            <Button render={<Link to={`/projects/${submitted.id}`} />} className="w-full">
              View this project
            </Button>

            <Button render={<Link to="/" />} variant="outline" className="w-full">
              Back to home
            </Button>
          </CardContent>
        </Card>
      </main>
    )
  }

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-2xl flex-col justify-center gap-4 p-6">
      <Card>
        <CardHeader>
          <CardTitle>Start a new project</CardTitle>
          <CardDescription>
            Tell us about the building you want. Everything except the last box is required.
          </CardDescription>
        </CardHeader>

        <CardContent>
          <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-5">
            <Field>
              <FieldLabel htmlFor="name">Project name</FieldLabel>
              <Input id="name" aria-invalid={Boolean(errors.name)} {...register('name')} />
              <FieldError errors={[errors.name]} />
            </Field>

            <Field>
              <FieldLabel htmlFor="location">Location</FieldLabel>
              <Input
                id="location"
                autoComplete="off"
                aria-invalid={Boolean(errors.location)}
                {...register('location')}
              />
              <FieldDescription>The town, address or plot the build is on.</FieldDescription>
              <FieldError errors={[errors.location]} />
            </Field>

            {/* Two to a row from `sm` up: eight short numeric answers in a single
                column makes a form that reads much longer than it is. */}
            <div className="grid gap-5 sm:grid-cols-2">
              <Field>
                <FieldLabel htmlFor="landSizePerches">Land size (perches)</FieldLabel>
                <Input
                  id="landSizePerches"
                  type="number"
                  inputMode="decimal"
                  step="0.01"
                  min="0"
                  aria-invalid={Boolean(errors.landSizePerches)}
                  {...register('landSizePerches', { valueAsNumber: true })}
                />
                <FieldError errors={[errors.landSizePerches]} />
              </Field>

              <Field>
                <FieldLabel htmlFor="budget">Budget (LKR)</FieldLabel>
                <Input
                  id="budget"
                  type="number"
                  inputMode="decimal"
                  step="0.01"
                  min="0"
                  aria-invalid={Boolean(errors.budget)}
                  {...register('budget', { valueAsNumber: true })}
                />
                <FieldError errors={[errors.budget]} />
              </Field>

              <Field>
                <FieldLabel htmlFor="floors">Floors</FieldLabel>
                <Input
                  id="floors"
                  type="number"
                  inputMode="numeric"
                  step="1"
                  min="1"
                  aria-invalid={Boolean(errors.floors)}
                  {...register('floors', { valueAsNumber: true })}
                />
                <FieldError errors={[errors.floors]} />
              </Field>

              <Field>
                <FieldLabel htmlFor="bedrooms">Bedrooms</FieldLabel>
                <Input
                  id="bedrooms"
                  type="number"
                  inputMode="numeric"
                  step="1"
                  min="0"
                  aria-invalid={Boolean(errors.bedrooms)}
                  {...register('bedrooms', { valueAsNumber: true })}
                />
                <FieldError errors={[errors.bedrooms]} />
              </Field>

              <Field>
                <FieldLabel htmlFor="bathrooms">Bathrooms</FieldLabel>
                <Input
                  id="bathrooms"
                  type="number"
                  inputMode="numeric"
                  step="1"
                  min="0"
                  aria-invalid={Boolean(errors.bathrooms)}
                  {...register('bathrooms', { valueAsNumber: true })}
                />
                <FieldError errors={[errors.bathrooms]} />
              </Field>

              <Field>
                <FieldLabel htmlFor="garageSpaces">Garage spaces</FieldLabel>
                <Input
                  id="garageSpaces"
                  type="number"
                  inputMode="numeric"
                  step="1"
                  min="0"
                  aria-invalid={Boolean(errors.garageSpaces)}
                  {...register('garageSpaces', { valueAsNumber: true })}
                />
                {/* A count rather than a checkbox, so "no garage" and "two cars"
                    are the same question. */}
                <FieldDescription>Enter 0 if you do not want a garage.</FieldDescription>
                <FieldError errors={[errors.garageSpaces]} />
              </Field>
            </div>

            <Field>
              <FieldLabel htmlFor="otherRequirements">Other requirements</FieldLabel>
              <Textarea
                id="otherRequirements"
                rows={4}
                aria-invalid={Boolean(errors.otherRequirements)}
                {...register('otherRequirements')}
              />
              <FieldDescription>
                Optional. Anything else you want us to know — finishes, accessibility, a solar
                setup.
              </FieldDescription>
              <FieldError errors={[errors.otherRequirements]} />
            </Field>

            {formError && (
              <p role="alert" className="text-destructive text-sm">
                {formError}
              </p>
            )}

            <Button type="submit" disabled={isSubmitting} className="w-full">
              {isSubmitting ? 'Submitting…' : 'Submit project'}
            </Button>

            <p className="text-muted-foreground text-center text-sm">
              <Link to="/" className="text-foreground underline underline-offset-4">
                Back to home
              </Link>
            </p>
          </form>
        </CardContent>
      </Card>
    </main>
  )
}
