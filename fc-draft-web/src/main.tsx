import React, { useEffect, useState } from 'react'
import ReactDOM from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App'
import { BrandLoader } from './components/BrandLoader'
import './styles.css'
import { applyStoredTheme } from './stores/themeStore'

applyStoredTheme()

function AppBootstrap() {
  const [ready, setReady] = useState(false)

  useEffect(() => {
    const timer = window.setTimeout(() => setReady(true), 900)
    return () => window.clearTimeout(timer)
  }, [])

  if (!ready) return <BrandLoader />

  return <BrowserRouter><App /></BrowserRouter>
}

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <AppBootstrap />
  </React.StrictMode>
)
