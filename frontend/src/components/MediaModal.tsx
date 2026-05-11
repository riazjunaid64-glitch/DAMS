import { useEffect, useState } from "react";
import { resolveMediaUrl } from "../api/api.ts";
import type { ProjectMedia, UnitMedia } from "../types/media.ts";

interface MediaModalProps {
  media: ProjectMedia | UnitMedia;
  onClose: () => void;
  onNext?: () => void;
  onPrevious?: () => void;
  isAdmin?: boolean;
  onDelete?: () => Promise<void>;
  onSetCover?: () => Promise<void>;
}

export default function MediaModal({
  media,
  onClose,
  onNext,
  onPrevious,
  isAdmin = false,
  onDelete,
  onSetCover,
}: MediaModalProps) {
  const [deleting, setDeleting] = useState(false);
  const [settingCover, setSettingCover] = useState(false);

  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };

    document.addEventListener("keydown", handleEscape);
    return () => document.removeEventListener("keydown", handleEscape);
  }, [onClose]);

  const handleDelete = async () => {
    if (!onDelete) return;
    
    setDeleting(true);
    try {
      await onDelete();
      onClose();
    } catch (error) {
      console.error("Delete failed:", error);
      alert("Failed to delete media. Please try again.");
    } finally {
      setDeleting(false);
    }
  };

  const handleSetCover = async () => {
    if (!onSetCover) return;
    
    setSettingCover(true);
    try {
      await onSetCover();
    } catch (error) {
      console.error("Set cover failed:", error);
      alert("Failed to set cover. Please try again.");
    } finally {
      setSettingCover(false);
    }
  };

  const formatFileSize = (bytes: number) => {
    if (bytes === 0) return "0 B";
    const k = 1024;
    const sizes = ["B", "KB", "MB", "GB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round(bytes / Math.pow(k, i) * 100) / 100 + " " + sizes[i];
  };

  const formatDate = (date: string) => {
    return new Date(date).toLocaleDateString("en-US", {
      month: "short",
      day: "numeric",
      year: "numeric",
    });
  };

  const isVideo = media.mediaType === "video";

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-black/90 backdrop-blur-sm"
        onClick={onClose}
      />

      {/* Modal Content */}
      <div className="relative z-10 flex max-h-[90vh] w-full max-w-6xl flex-col gap-4 animate-scale-in">
        {/* Header */}
        <div className="flex items-center justify-between">
          <h3 className="text-lg font-semibold text-white">
            {media.originalFileName || `Media ${media.id}`}
          </h3>
          <div className="flex items-center gap-2">
            {isAdmin && (
              <>
                {!media.isCover && onSetCover && (
                  <button
                    onClick={handleSetCover}
                    disabled={settingCover}
                    className="flex items-center gap-2 rounded-lg bg-white/10 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-white/20 disabled:opacity-50"
                  >
                    {settingCover ? (
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="animate-spin">
                        <path d="M21 12a9 9 0 1 1-6.219-8.56" />
                      </svg>
                    ) : (
                      <>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                          <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
                        </svg>
                        Set Cover
                      </>
                    )}
                  </button>
                )}
                {onDelete && (
                  <button
                    onClick={handleDelete}
                    disabled={deleting}
                    className="flex items-center gap-2 rounded-lg bg-rose-500/20 px-3 py-2 text-sm font-medium text-rose-300 transition-colors hover:bg-rose-500/30 disabled:opacity-50"
                  >
                    {deleting ? (
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="animate-spin">
                        <path d="M21 12a9 9 0 1 1-6.219-8.56" />
                      </svg>
                    ) : (
                      <>
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                          <line x1="18" y1="6" x2="6" y2="18" />
                          <line x1="6" y1="6" x2="18" y2="18" />
                        </svg>
                        Delete
                      </>
                    )}
                  </button>
                )}
              </>
            )}
            <button
              onClick={onClose}
              className="flex h-10 w-10 items-center justify-center rounded-lg bg-white/10 text-white transition-colors hover:bg-white/20"
            >
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <line x1="18" y1="6" x2="6" y2="18" />
                <line x1="6" y1="6" x2="18" y2="18" />
              </svg>
            </button>
          </div>
        </div>

        {/* Main Content */}
        <div className="flex flex-1 flex-col gap-4 overflow-hidden lg:flex-row">
          {/* Media Preview */}
          <div className="flex flex-1 items-center justify-center bg-black/50 rounded-2xl overflow-hidden">
            {isVideo ? (
              <video
                src={resolveMediaUrl(media.mediaUrl)}
                controls
                className="max-h-full max-w-full"
                autoPlay
              />
            ) : (
              <img
                src={resolveMediaUrl(media.mediaUrl)}
                alt={media.altText || media.originalFileName || ""}
                className="max-h-full max-w-full object-contain"
              />
            )}
          </div>

          {/* Info Panel */}
          <div className="w-full space-y-4 rounded-2xl bg-[var(--surface-glass)] p-6 lg:w-80">
            {/* Cover Badge */}
            {media.isCover && (
              <div className="flex items-center gap-2 rounded-lg bg-gradient-to-r from-indigo-500/20 to-violet-600/20 px-3 py-2 border border-indigo-500/30">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="text-indigo-300">
                  <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
                </svg>
                <span className="text-sm font-semibold text-indigo-300">Cover Image</span>
              </div>
            )}

            {/* Metadata */}
            <div className="space-y-3">
              <div>
                <label className="text-xs font-medium text-[var(--text-muted)] uppercase tracking-wider">
                  File Name
                </label>
                <p className="mt-1 text-sm text-[var(--text-primary)]">
                  {media.originalFileName || `Media ${media.id}`}
                </p>
              </div>

              <div>
                <label className="text-xs font-medium text-[var(--text-muted)] uppercase tracking-wider">
                  Type
                </label>
                <p className="mt-1 text-sm text-[var(--text-primary)] capitalize">
                  {media.mediaType}
                </p>
              </div>

              <div>
                <label className="text-xs font-medium text-[var(--text-muted)] uppercase tracking-wider">
                  Size
                </label>
                <p className="mt-1 text-sm text-[var(--text-primary)]">
                  {formatFileSize(media.fileSize)}
                </p>
              </div>

              {media.width && media.height && (
                <div>
                  <label className="text-xs font-medium text-[var(--text-muted)] uppercase tracking-wider">
                    Dimensions
                  </label>
                  <p className="mt-1 text-sm text-[var(--text-primary)]">
                    {media.width} × {media.height} px
                  </p>
                </div>
              )}

              <div>
                <label className="text-xs font-medium text-[var(--text-muted)] uppercase tracking-wider">
                  Uploaded
                </label>
                <p className="mt-1 text-sm text-[var(--text-primary)]">
                  {formatDate(media.uploadedAt)}
                </p>
              </div>

              {media.altText && (
                <div>
                  <label className="text-xs font-medium text-[var(--text-muted)] uppercase tracking-wider">
                    Alt Text
                  </label>
                  <p className="mt-1 text-sm text-[var(--text-primary)]">
                    {media.altText}
                  </p>
                </div>
              )}

              {media.description && (
                <div>
                  <label className="text-xs font-medium text-[var(--text-muted)] uppercase tracking-wider">
                    Description
                  </label>
                  <p className="mt-1 text-sm text-[var(--text-primary)]">
                    {media.description}
                  </p>
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Navigation */}
        {(onNext || onPrevious) && (
          <div className="flex items-center justify-center gap-4">
            {onPrevious && (
              <button
                onClick={onPrevious}
                className="flex h-12 w-12 items-center justify-center rounded-full bg-white/10 text-white transition-colors hover:bg-white/20"
              >
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <polyline points="15 18 9 12 15 6" />
                </svg>
              </button>
            )}
            {onNext && (
              <button
                onClick={onNext}
                className="flex h-12 w-12 items-center justify-center rounded-full bg-white/10 text-white transition-colors hover:bg-white/20"
              >
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <polyline points="9 18 15 12 9 6" />
                </svg>
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
