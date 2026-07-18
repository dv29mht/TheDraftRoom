import { BrandMark } from './BrandMark'

export function BrandLoader() {
  return (
    <div className="brand-loader" role="status" aria-live="polite" aria-label="Loading ROSTR">
      <div className="loader-arena" aria-hidden="true">
        <span className="loader-gate loader-gate-one" />
        <span className="loader-gate loader-gate-two" />
        <span className="loader-gate loader-gate-three" />
        <span className="loader-scan" />
      </div>
      <BrandMark />
      <div className="loader-progress" aria-hidden="true"><span /></div>
      <p>Draft. Strategize. Dominate.</p>
    </div>
  )
}
