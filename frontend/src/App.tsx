import { BrowserRouter } from 'react-router-dom'

import { RegisterPage } from '@/pages/RegisterPage'

export default function App() {
  // Routes arrive in the routing commit; for now the register page is the app.
  return (
    <BrowserRouter>
      <RegisterPage />
    </BrowserRouter>
  )
}
