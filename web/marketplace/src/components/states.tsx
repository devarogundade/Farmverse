import { sprite } from '@farmverse/shared/gameTheme';
import { useEffect, useRef, type ReactNode } from 'react';

/** Card-shaped placeholders while a page of items loads. */
export function SkeletonGrid({ count = 8 }: { count?: number }) {
  return (
    <div className="grid" aria-busy="true" aria-label="Loading items">
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className="nft-card fv-frame skeleton-card">
          <span className="sk sk-model" />
          <span className="sk sk-foot" />
        </div>
      ))}
    </div>
  );
}

/** The item page while it loads: hero frame + side panels. */
export function SkeletonDetail() {
  return (
    <div className="detail" aria-busy="true" aria-label="Loading item">
      <div className="hero fv-frame lg skeleton-card">
        <span className="sk sk-title" />
        <span className="sk sk-model tall" />
      </div>
      <div className="side">
        {[0, 1, 2].map((i) => (
          <div key={i} className={`card fv-panel${i === 2 ? ' history-card' : ''}`}>
            <span className="sk sk-line w40" />
            <span className="sk sk-line" />
            <span className="sk sk-line w70" />
          </div>
        ))}
      </div>
    </div>
  );
}

/** Something failed: say so plainly and offer a retry. */
export function ErrorState({ title = 'Couldn’t load this', error, onRetry }: { title?: string; error?: unknown; onRetry?: () => void }) {
  return (
    <div className="empty" role="alert">
      <img className="empty-art" src={sprite('Icon_ImageIcon_Info')} alt="" />
      <h2 className="fv-title">{title}</h2>
      <p className="muted">{(error as Error)?.message || 'Please try again in a moment.'}</p>
      {onRetry && (
        <button className="fv-btn green" onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  );
}

export function EmptyState({ art = 'Home_Npc', title, children }: { art?: string; title: string; children?: ReactNode }) {
  return (
    <div className="empty">
      <img className="empty-art" src={sprite(art)} alt="" />
      <h2 className="fv-title">{title}</h2>
      {children}
    </div>
  );
}

/**
 * Pagination footer: loads the next page when it scrolls into view, with a button as the
 * keyboard / no-IntersectionObserver fallback.
 */
export function LoadMore({ hasMore, loading, onMore, shown, total }: { hasMore: boolean; loading: boolean; onMore: () => void; shown: number; total: number }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const el = ref.current;
    if (!el || !hasMore || loading || !('IntersectionObserver' in window)) return;
    const io = new IntersectionObserver((e) => e[0]?.isIntersecting && onMore(), { rootMargin: '400px 0px' });
    io.observe(el);
    return () => io.disconnect();
  }, [hasMore, loading, onMore]);
  return (
    <div ref={ref} className="load-more">
      <span className="muted">
        Showing {shown} of {total}
      </span>
      {hasMore && (
        <button className="fv-btn blue sm" onClick={onMore} disabled={loading}>
          {loading ? 'Loading…' : 'Load more'}
        </button>
      )}
    </div>
  );
}
