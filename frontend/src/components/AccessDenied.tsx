import { Link } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { ROLE_LABELS, type Role } from '@/lib/roles'

/**
 * Shown when a signed-in user reaches something their role may not use (US-03).
 *
 * Says plainly what happened and who the page is for, rather than bouncing the
 * user somewhere else and leaving them to guess. This is the same answer the
 * service gives: a 403, not a blank screen and not a silent redirect.
 */
export function AccessDenied({
  role,
  allowedRoles,
}: {
  role: Role
  allowedRoles: readonly Role[]
}) {
  return (
    <main className="flex min-h-svh items-center justify-center p-6">
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle>You do not have access to this page</CardTitle>
          <CardDescription>
            It is open to {allowedRoles.map((allowed) => ROLE_LABELS[allowed]).join(' and ')}. You
            are signed in as {ROLE_LABELS[role]}.
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          <p className="text-muted-foreground text-sm">
            If you believe your account should have this access, ask an administrator to change
            your role — you cannot change it yourself.
          </p>

          <Button render={<Link to="/" />} variant="outline" className="w-full">
            Back to home
          </Button>
        </CardContent>
      </Card>
    </main>
  )
}
