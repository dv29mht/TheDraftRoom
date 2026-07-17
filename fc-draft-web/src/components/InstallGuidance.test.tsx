import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InstallGuidance } from './InstallGuidance'
import { useAppLifecycleStore, type BeforeInstallPromptEvent } from '../stores/appLifecycleStore'

const originalUserAgent = navigator.userAgent

function setUserAgent(value: string) {
  Object.defineProperty(window.navigator, 'userAgent', { value, configurable: true })
}

function resetLifecycle() {
  useAppLifecycleStore.setState({
    online: true,
    updateReady: false,
    updateDismissed: false,
    installPrompt: null
  })
}

describe('InstallGuidance (PR-22 §12.2)', () => {
  beforeEach(resetLifecycle)
  afterEach(() => setUserAgent(originalUserAgent))

  it('walks iPhone users through Add to Home Screen (Safari has no install prompt)', () => {
    setUserAgent('Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 Version/17.5 Mobile/15E148 Safari/604.1')
    render(<InstallGuidance />)

    const steps = screen.getAllByRole('listitem')
    expect(steps[0]).toHaveTextContent(/safari/i)
    expect(steps[1]).toHaveTextContent(/share/i)
    expect(steps[2]).toHaveTextContent(/add to home screen/i)
  })

  it('points other browsers at their own install affordance', () => {
    render(<InstallGuidance />)
    expect(screen.getByText(/install the draft room…/i)).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('offers the captured native prompt and reports success', async () => {
    const prompt = vi.fn().mockResolvedValue(undefined)
    const installEvent = {
      prompt,
      userChoice: Promise.resolve({ outcome: 'accepted' as const })
    } as unknown as BeforeInstallPromptEvent
    useAppLifecycleStore.setState({ installPrompt: installEvent })
    render(<InstallGuidance />)

    await userEvent.click(screen.getByRole('button', { name: /install the draft room/i }))

    expect(prompt).toHaveBeenCalledTimes(1)
    expect(await screen.findByRole('status')).toHaveTextContent(/installed/i)
    // The one-shot event is consumed either way.
    expect(useAppLifecycleStore.getState().installPrompt).toBeNull()
  })

  it('shows the installed state when already running standalone', () => {
    const matchMedia = vi.spyOn(window, 'matchMedia').mockImplementation((query) => ({
      matches: query === '(display-mode: standalone)',
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn()
    }) as unknown as MediaQueryList)

    render(<InstallGuidance />)
    expect(screen.getByRole('status')).toHaveTextContent(/installed/i)

    matchMedia.mockRestore()
  })
})
