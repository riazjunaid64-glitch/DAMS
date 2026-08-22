import { Component } from "react";
import type { ErrorInfo, ReactNode } from "react";

interface Props {
  children: ReactNode;
  /** Changing this clears a caught error — pass the route so navigating away recovers. */
  resetKey?: string;
}

interface State {
  error: Error | null;
}

/**
 * Stops one broken screen from taking the whole application down with it.
 *
 * React unmounts the entire tree when a render throws. Without a boundary that leaves a blank
 * page: no message, no stack, nothing to report — the failure is invisible in exactly the moment
 * somebody needs to describe it. Catching it here keeps the error on screen and in the console,
 * and keeps the rest of the app navigable so the user is not stranded.
 */
export default class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Kept so the component stack survives in the console even after the fallback replaces the UI.
    console.error("Unhandled render error:", error, info.componentStack);
  }

  componentDidUpdate(previous: Props) {
    // A different route is a different screen, so give it a clean slate rather than holding the
    // previous page's error over it.
    if (this.state.error && previous.resetKey !== this.props.resetKey) {
      this.setState({ error: null });
    }
  }

  render() {
    const { error } = this.state;
    if (!error) return this.props.children;

    return (
      <div className="flex min-h-[60vh] items-center justify-center p-6">
        <div className="w-full max-w-xl rounded-2xl border border-rose-500/30 bg-rose-500/[0.06] p-6">
          <h2 className="text-lg font-semibold text-[var(--text-heading)]">This screen failed to load</h2>
          <p className="mt-2 text-sm text-[var(--text-muted)]">
            The rest of the app still works — use the menu to go somewhere else, or reload.
          </p>
          <p className="mt-4 break-words rounded-lg bg-black/30 p-3 font-mono text-xs text-rose-300">
            {error.message || String(error)}
          </p>
          <div className="mt-4 flex gap-2">
            <button
              type="button"
              onClick={() => this.setState({ error: null })}
              className="rounded-xl border border-[var(--border)] px-4 py-2 text-sm font-semibold"
            >
              Try again
            </button>
            <button
              type="button"
              onClick={() => window.location.reload()}
              className="rounded-xl border border-[var(--border)] px-4 py-2 text-sm font-semibold"
            >
              Reload
            </button>
          </div>
        </div>
      </div>
    );
  }
}
