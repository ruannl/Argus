export default function OverviewLoading() {
  return (
    <div aria-busy="true" aria-live="polite" className="mt-8 space-y-3">
      <p className="text-sm font-semibold text-muted">Loading the selected Overview snapshot…</p>
      <div className="h-24 animate-pulse rounded-[1.5rem] border border-line bg-surface" />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {[0, 1, 2, 3].map((item) => (
          <div
            className="h-24 animate-pulse rounded-[1.25rem] border border-line bg-surface"
            key={item}
          />
        ))}
      </div>
      <p className="text-xs text-muted">
        The previous figures are hidden until the new snapshot is ready.
      </p>
    </div>
  );
}
