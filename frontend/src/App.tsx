import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'

import { ProtectedRoute } from '@/auth/ProtectedRoute'
import { RoleRoute } from '@/auth/RoleRoute'
import { AdminUsersPage } from '@/pages/AdminUsersPage'
import { ForgotPasswordPage } from '@/pages/ForgotPasswordPage'
import { HomePage } from '@/pages/HomePage'
import { LandingPage } from '@/pages/LandingPage'
import { LoginPage } from '@/pages/LoginPage'
import { NewProjectPage } from '@/pages/NewProjectPage'
import { ProfilePage } from '@/pages/ProfilePage'
import { ProjectDetailPage } from '@/pages/ProjectDetailPage'
import { ProjectsPage } from '@/pages/ProjectsPage'
import { RegisterPage } from '@/pages/RegisterPage'
import { ResetPasswordPage } from '@/pages/ResetPasswordPage'
import { StaffDirectoryPage } from '@/pages/StaffDirectoryPage'
import { ADMIN_ROLES, CLIENT_ROLES, PROJECT_STAFF_ROLES } from '@/lib/roles'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/login" element={<LoginPage />} />
        {/* Both open to anyone: a forgotten password is exactly the situation
            with no session to guard the route with. The reset page reads its
            token from the query string the emailed link carries. */}
        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        {/* Public: the first thing a visitor with no session sees. A signed-in
            visitor is forwarded on to /home from here. */}
        <Route path="/" element={<LandingPage />} />
        <Route
          path="/home"
          element={
            <ProtectedRoute>
              <HomePage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/profile"
          element={
            <ProtectedRoute>
              <ProfilePage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/projects/new"
          element={
            <RoleRoute allowedRoles={CLIENT_ROLES}>
              <NewProjectPage />
            </RoleRoute>
          }
        />
        {/* Both open to any signed-in user. Which projects they may see is a
            per-project question — the owning client, the assigned staff, or an
            Admin — and only the service can answer it, so a role guard here
            would be guessing. */}
        <Route
          path="/projects"
          element={
            <ProtectedRoute>
              <ProjectsPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/projects/:projectId"
          element={
            <ProtectedRoute>
              <ProjectDetailPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/admin/users"
          element={
            <RoleRoute allowedRoles={ADMIN_ROLES}>
              <AdminUsersPage />
            </RoleRoute>
          }
        />
        <Route
          path="/directory"
          element={
            <RoleRoute allowedRoles={PROJECT_STAFF_ROLES}>
              <StaffDirectoryPage />
            </RoleRoute>
          }
        />
        {/* Anything unrecognised goes to the front door: the public landing
            page when there is no session, or on to /home when there is. */}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
