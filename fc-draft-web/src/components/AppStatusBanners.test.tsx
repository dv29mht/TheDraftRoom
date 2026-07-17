import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AppStatusBanners } from './AppStatusBanners'
import { registerServiceWorkerHooks, useAppLifecycleStore } from '../stores/appLifecycleStore'

function resetLifecycle() {
  useAppLifecycleStore.setState({
    online: true,
    updateReady: false,
    updateDismissed: false,
    installPrompt: null
  })
}

describe('AppStatusBanners (PR-22 §12.2)', () => {
  beforeEach(resetLifecycle)

  it('renders nothing while online and up to date', () => {
    const { container } = render(<AppStatusBanners />)
    expect(container).toBeEmptyDOMElement()
  })

  it('announces the offline state in a live region', () => {
    useAppLifecycleStore.setState({ online: false })
    render(<AppStatusBanners />)
    const banner = screen.getByRole('status')
    expect(banner).toHaveTextContent(/you.re offline/i)
    expect(banner).toHaveTextContent(/paused until the connection returns/i)
  })

  it('offers refresh and later when an update is ready', async () => {
    const applyUpdate = vi.fn()
    registerServiceWorkerHooks({ applyUpdate })
    useAppLifecycleStore.setState({ updateReady: true })
    render(<AppStatusBanners />)

    expect(screen.getByRole('status')).toHaveTextContent(/new version of the draft room is ready/i)
    await userEvent.click(screen.getByRole('button', { name: /refresh now/i }))
    expect(applyUpdate).toHaveBeenCalledTimes(1)
  })

  it('hides after Later until the next trigger', async () => {
    useAppLifecycleStore.setState({ updateReady: true })
    render(<AppStatusBanners />)

    await userEvent.click(screen.getByRole('button', { name: /later/i }))
    expect(screen.queryByRole('status')).not.toBeInTheDocument()

    // A fresh mismatch (e.g. the API contract check) re-raises the prompt.
    useAppLifecycleStore.getState().showUpdatePrompt()
    expect(await screen.findByRole('status')).toHaveTextContent(/new version/i)
  })

  it('prioritises the offline message over the update prompt', () => {
    useAppLifecycleStore.setState({ online: false, updateReady: true })
    render(<AppStatusBanners />)
    const banners = screen.getAllByRole('status')
    expect(banners).toHaveLength(1)
    expect(banners[0]).toHaveTextContent(/offline/i)
  })
})
