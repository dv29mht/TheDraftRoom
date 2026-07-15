import { AxiosError, AxiosHeaders } from 'axios'
import { beforeEach, describe, expect, it } from 'vitest'
import { api, getApiError } from './api'

function axiosErrorWith(data: unknown, status = 400): AxiosError {
  const error = new AxiosError('request failed', 'ERR_BAD_REQUEST')
  error.response = {
    data,
    status,
    statusText: '',
    headers: {},
    config: { headers: new AxiosHeaders() },
  } as AxiosError['response']
  return error
}

describe('getApiError', () => {
  it('surfaces the first field-level validation message', () => {
    const error = axiosErrorWith({
      errors: { Email: ['A valid email address is required.'], Password: ['Required.'] },
    })
    expect(getApiError(error)).toBe('A valid email address is required.')
  })

  it('falls back to the problem detail when there are no field errors', () => {
    const error = axiosErrorWith({ detail: 'This account has been deactivated.' }, 403)
    expect(getApiError(error)).toBe('This account has been deactivated.')
  })

  it('returns a generic message for a non-HTTP failure', () => {
    expect(getApiError(new Error('offline'))).toBe('Something went wrong. Please try again.')
  })
})

describe('auth request interceptor', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const fulfilled = (api.interceptors.request as any).handlers[0].fulfilled as (
    config: { headers: Record<string, unknown> },
  ) => { headers: Record<string, unknown> }

  beforeEach(() => localStorage.clear())

  it('attaches the bearer token when one is stored', () => {
    localStorage.setItem('fc-draft-token', 'stored-token')
    const config = fulfilled({ headers: {} })
    expect(config.headers.Authorization).toBe('Bearer stored-token')
  })

  it('sends no Authorization header when signed out', () => {
    const config = fulfilled({ headers: {} })
    expect(config.headers.Authorization).toBeUndefined()
  })
})
