import type { ReactNode } from 'react'

import { Footer } from '@/components/Footer'
import { Header } from '@/components/Header'
import { PageBar } from '@/components/PageBar'

/**
 * The frame every authenticated page sits in: the black header with the role's
 * own navigation, the breadcrumb strip, the page itself, and the black footer —
 * kept at the foot of the window even when the page is short.
 *
 * Pages supply only their own `<main>`; the frame owns everything around it, so
 * no page repeats a header, a footer, or the arrangement that keeps them apart.
 * `ProtectedRoute` puts it around every authenticated route, and `RoleRoute`
 * sits inside that, so role-gated pages — and the "not for your role" notice —
 * get it too.
 */
export function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-svh flex-col bg-background">
      {/* First stop for a keyboard user: past the navigation, straight to the page. */}
      <a
        href="#content"
        className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:rounded-lg focus:bg-brand focus:px-4 focus:py-2 focus:text-sm focus:text-brand-foreground"
      >
        Skip to content
      </a>

      <Header />
      <PageBar />

      <div id="content" className="flex-1 bg-muted/30">
        {children}
      </div>

      <Footer />
    </div>
  )
}
