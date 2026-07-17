import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { AppStatusBanners } from './AppStatusBanners'
import { useAppLifecycleStore } from '../stores/appLifecycleStore'

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

  it('renders nothing while online', () => {
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

  it('does not surface an update prompt even when a new build is ready', () => {
    useAppLifecycleStore.setState({ updateReady: true })
    const { container } = render(<AppStatusBanners />)
    expect(container).toBeEmptyDOMElement()
  })
})
