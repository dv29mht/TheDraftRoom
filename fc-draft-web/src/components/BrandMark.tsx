export function BrandMark({ compact = false }: { compact?: boolean }) {
  return (
    // role="img": aria-label is prohibited on a generic div (ARIA, caught by axe in PR-22);
    // the mark + wordmark read as one image named "The Draft Room".
    <div className="brand" role="img" aria-label="The Draft Room">
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
