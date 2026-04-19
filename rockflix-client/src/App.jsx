import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { AuthProvider, useAuth } from './context/AuthContext'
import LoginPage from './pages/LoginPage'
import RegisterPage from './pages/RegisterPage'
import HomePage from './pages/HomePage'

function PrivateRoute({ children }) {
  const { user } = useAuth()
  return user ? children : <Navigate to="/login" replace />
}

function PublicRoute({ children }) {
  const { user } = useAuth()
  return user ? <Navigate to="/" replace /> : children
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<PublicRoute><LoginPage /></PublicRoute>} />
      <Route path="/register" element={<PublicRoute><RegisterPage /></PublicRoute>} />
      <Route path="/" element={<PrivateRoute><HomePage /></PrivateRoute>} />
      {/* These pages will be built next */}
      <Route path="/library" element={<PrivateRoute><div className="p-8 text-white">Library — coming soon</div></PrivateRoute>} />
      <Route path="/movie/:id" element={<PrivateRoute><div className="p-8 text-white">Movie detail — coming soon</div></PrivateRoute>} />
      <Route path="/watch/movie/:id" element={<PrivateRoute><div className="p-8 text-white">Player — coming soon</div></PrivateRoute>} />
      <Route path="/watch/episode/:id" element={<PrivateRoute><div className="p-8 text-white">Episode player — coming soon</div></PrivateRoute>} />
    </Routes>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </BrowserRouter>
  )
}
