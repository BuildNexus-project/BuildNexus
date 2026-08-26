import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'

import { ProtectedRoute } from '@/auth/ProtectedRoute'
import { RoleRoute } from '@/auth/RoleRoute'
import { AdminUsersPage } from '@/pages/AdminUsersPage'
import { HomePage } from '@/pages/HomePage'
import { LoginPage } from '@/pages/LoginPage'
import { ProfilePage } from '@/pages/ProfilePage'
import { RegisterPage } from '@/pages/RegisterPage'
import { StaffDirectoryPage } from '@/pages/StaffDirectoryPage'
import { ADMIN_ROLES, PROJECT_STAFF_ROLES } from '@/lib/roles'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/login" element={<LoginPage />} />
        <Route
          path="/"
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
        {/* Anything unrecognised goes home, which in turn redirects to /login
            when there is no session. */}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
