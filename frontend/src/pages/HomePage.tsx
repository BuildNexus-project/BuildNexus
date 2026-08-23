import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { ROLE_LABELS } from '@/lib/roles'

/**
 * Placeholder landing page for a signed-in user. The real role-aware dashboard
 * is US-29; this only proves the session survived and shows who is signed in.
 */
export function HomePage() {
  const { user, signOut } = useAuth()

  if (!user) {
    return null
  }

  return (
    <main className="flex min-h-svh items-center justify-center p-6">
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle>
            Logged in as {user.fullName} ({ROLE_LABELS[user.role]})
          </CardTitle>
          <CardDescription>{user.email}</CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          <p className="text-muted-foreground text-sm">
            Your dashboard arrives in a later story. Until then, this page just confirms your
            session is active.
          </p>

          <Button render={<Link to="/profile" />} className="w-full">
            Manage your profile
          </Button>

          <Button variant="outline" onClick={signOut} className="w-full">
            Sign out
          </Button>
        </CardContent>
      </Card>
    </main>
  )
}
