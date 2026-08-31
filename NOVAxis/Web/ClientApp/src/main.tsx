import { useEffect, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Route, Routes, useLocation } from 'react-router-dom'

import { api, ApiError, WebUserDto } from './api'
import { Discord } from './Icons'
import { ToastProvider } from './Toast'
import { UserContext } from './user'
import { GuildPicker } from './pages/GuildPicker'
import { PlayerPage } from './pages/PlayerPage'
import { DownloadsPage } from './pages/DownloadsPage'

import './styles.css'

/**
 * Loads the session before anything renders. Without one, the whole app is a
 * single invitation to sign in through Discord.
 */
function AuthGate({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<WebUserDto | null>(null)
  const [status, setStatus] = useState<'loading' | 'anonymous' | 'ready'>('loading')
  const location = useLocation()

  useEffect(() => {
    api
      .me()
      .then(me => {
        setUser(me)
        setStatus('ready')
      })
      .catch(error => {
        setStatus(error instanceof ApiError && error.status === 401 ? 'anonymous' : 'anonymous')
      })
  }, [])

  if (status === 'loading') {
    return (
      <div className="screen">
        <div className="pulse-ring" aria-label="Načítání" />
      </div>
    )
  }

  if (status === 'anonymous') {
    const returnUrl = encodeURIComponent(location.pathname + location.search)

    return (
      <div className="screen login">
        <div className="login-card">
          <div className="login-mark" aria-hidden="true" />
          <p className="eyebrow">NOVAXIS · PŘEHRÁVAČ</p>
          <h1>Ovládej jádro přímo z&nbsp;prohlížeče</h1>
          <p className="login-sub">
            Fronta, vyhledávání i přehrávání na jednom místě. Přihlas se svým účtem
            na Discordu — přístup mají jen členové serveru.
          </p>
          <a className="btn-discord" href={`/api/auth/login?returnUrl=${returnUrl}`}>
            <Discord size={20} />
            Přihlásit se přes Discord
          </a>
        </div>
      </div>
    )
  }

  return <UserContext.Provider value={user}>{children}</UserContext.Provider>
}

function App() {
  return (
    <AuthGate>
      <Routes>
        <Route path="/" element={<GuildPicker />} />
        <Route path="/g/:guildId" element={<PlayerPage />} />
        <Route path="/downloads" element={<DownloadsPage />} />
        <Route path="*" element={<GuildPicker />} />
      </Routes>
    </AuthGate>
  )
}

createRoot(document.getElementById('root')!).render(
  <BrowserRouter>
    <ToastProvider>
      <App />
    </ToastProvider>
  </BrowserRouter>,
)
