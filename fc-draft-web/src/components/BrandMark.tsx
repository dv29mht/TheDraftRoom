export function BrandMark({ compact = false }: { compact?: boolean }) {
  return (
    <div className="brand" aria-label="The Draft Room">
      <img className="brand-mark" src="/mark.svg" width="45" height="45" alt="" aria-hidden="true" />
      {!compact && (
        <span className="brand-wordmark" aria-hidden="true">
          <strong>THE DRAFT</strong>
          <small>ROOM</small>
        </span>
      )}
    </div>
  )
}
