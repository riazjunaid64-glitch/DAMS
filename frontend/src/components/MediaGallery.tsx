import { useState } from "react";
import { resolveMediaUrl } from "../api/api.ts";
import type { ProjectMedia, UnitMedia, MediaCategory, UpdateMediaDto } from "../types/media";
import MediaModal from "./MediaModal.tsx";

interface MediaGalleryProps {
  media: (ProjectMedia | UnitMedia)[];
  onDelete?: (mediaId: number) => Promise<void>;
  onSetCover?: (mediaId: number) => Promise<void>;
  onUpdate?: (mediaId: number, updateDto: UpdateMediaDto) => Promise<void>;
  isAdmin?: boolean;
  loading?: boolean;
}

const categoryLabels: Record<MediaCategory, string> = {
  1: "Gallery",
  2: "Thumbnail",
  3: "Floor Plan",
  4: "Brochure",
  5: "Construction",
  6: "Interior",
  7: "Exterior",
  8: "Document",
  9: "Video",
};

const categoryColors: Record<MediaCategory, string> = {
  1: "bg-indigo-500/10 text-indigo-300 border-indigo-500/20",
  2: "bg-emerald-500/10 text-emerald-300 border-emerald-500/20",
  3: "bg-amber-500/10 text-amber-300 border-amber-500/20",
  4: "bg-rose-500/10 text-rose-300 border-rose-500/20",
  5: "bg-cyan-500/10 text-cyan-300 border-cyan-500/20",
  6: "bg-purple-500/10 text-purple-300 border-purple-500/20",
  7: "bg-orange-500/10 text-orange-300 border-orange-500/20",
  8: "bg-slate-500/10 text-slate-300 border-slate-500/20",
  9: "bg-pink-500/10 text-pink-300 border-pink-500/20",
};

export default function MediaGallery({
  media,
  onDelete,
  onSetCover,
  isAdmin = false,
  loading = false,
}: MediaGalleryProps) {
  const [selectedMedia, setSelectedMedia] = useState<ProjectMedia | UnitMedia | null>(null);
  const [deletingId, setDeletingId] = useState<number | null>(null);
  const [settingCoverId, setSettingCoverId] = useState<number | null>(null);

  const handleDelete = async (mediaId: number) => {
    if (!onDelete) return;
    
    setDeletingId(mediaId);
    try {
      await onDelete(mediaId);
    } catch (error) {
      console.error("Delete failed:", error);
      alert("Failed to delete media. Please try again.");
    } finally {
      setDeletingId(null);
    }
  };

  const handleSetCover = async (mediaId: number) => {
    if (!onSetCover) return;
    
    setSettingCoverId(mediaId);
    try {
      await onSetCover(mediaId);
    } catch (error) {
      console.error("Set cover failed:", error);
      alert("Failed to set cover. Please try again.");
    } finally {
      setSettingCoverId(null);
    }
  };

  const formatFileSize = (bytes: number) => {
    if (bytes === 0) return "0 B";
    const k = 1024;
    const sizes = ["B", "KB", "MB", "GB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round(bytes / Math.pow(k, i) * 100) / 100 + " " + sizes[i];
  };

  if (loading) {
    return (
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
        {[...Array(8)].map((_, i) => (
          <div key={i} className="glass-card aspect-square animate-pulse" />
        ))}
      </div>
    );
  }

  if (media.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-16 text-center">
        <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-[var(--surface-glass)] border border-[var(--border)]">
          <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[var(--text-muted)]" strokeLinecap="round" strokeLinejoin="round">
            <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
            <circle cx="8.5" cy="8.5" r="1.5" />
            <polyline points="21 15 16 10 5 21" />
          </svg>
        </div>
        <h3 className="text-base font-semibold text-[var(--text-heading)]">No media yet</h3>
        <p className="mt-1 text-sm text-[var(--text-secondary)]">
          Upload images, videos, and documents to get started.
        </p>
      </div>
    );
  }

  return (
    <>
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
        {media.map((item, index) => {
          const isVideo = item.mediaType === "video";
          const isCover = item.isCover;
          const categoryColor = categoryColors[item.category] || categoryColors[1];
          const categoryLabel = categoryLabels[item.category] || "Gallery";

          return (
            <div
              key={item.id}
              className="group glass-card overflow-hidden animate-fade-in-up"
              style={{ animationDelay: `${index * 50}ms` }}
            >
              {/* Media Preview */}
              <div
                className="relative aspect-square cursor-pointer overflow-hidden bg-[var(--surface-glass)]"
                onClick={() => setSelectedMedia(item)}
              >
                {isVideo ? (
                  <div className="flex h-full w-full items-center justify-center">
                    <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[var(--text-muted)]" strokeLinecap="round" strokeLinejoin="round">
                      <polygon points="5 3 19 12 5 21 5 3" />
                    </svg>
                  </div>
                ) : (
                  <img
                    src={resolveMediaUrl(item.mediaUrl)}
                    alt={item.altText || item.originalFileName || ""}
                    className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-105"
                    loading="lazy"
                  />
                )}

                {/* Cover Badge */}
                {isCover && (
                  <div className="absolute left-2 top-2 flex items-center gap-1.5 rounded-full bg-gradient-to-r from-indigo-500 to-violet-600 px-2.5 py-1 shadow-md">
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" className="text-white">
                      <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
                    </svg>
                    <span className="text-[10px] font-semibold text-white">Cover</span>
                  </div>
                )}

                {/* Category Badge */}
                <div className={`absolute right-2 top-2 rounded-full border px-2 py-0.5 text-[10px] font-semibold backdrop-blur-sm ${categoryColor}`}>
                  {categoryLabel}
                </div>

                {/* Hover Overlay */}
                <div className="absolute inset-0 bg-black/50 opacity-0 transition-opacity duration-200 group-hover:opacity-100" />
                
                {/* Quick Actions */}
                {isAdmin && (
                  <div className="absolute bottom-2 right-2 flex gap-1.5 opacity-0 transition-opacity duration-200 group-hover:opacity-100">
                    {!isCover && onSetCover && (
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          handleSetCover(item.id);
                        }}
                        disabled={settingCoverId === item.id}
                        className="flex h-8 w-8 items-center justify-center rounded-lg bg-white/10 backdrop-blur-sm text-white transition-colors hover:bg-white/20 disabled:opacity-50"
                        title="Set as cover"
                      >
                        {settingCoverId === item.id ? (
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="animate-spin">
                            <path d="M21 12a9 9 0 1 1-6.219-8.56" />
                          </svg>
                        ) : (
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
                          </svg>
                        )}
                      </button>
                    )}
                    {onDelete && (
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          handleDelete(item.id);
                        }}
                        disabled={deletingId === item.id}
                        className="flex h-8 w-8 items-center justify-center rounded-lg bg-white/10 backdrop-blur-sm text-rose-300 transition-colors hover:bg-rose-500/30 disabled:opacity-50"
                        title="Delete"
                      >
                        {deletingId === item.id ? (
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="animate-spin">
                            <path d="M21 12a9 9 0 1 1-6.219-8.56" />
                          </svg>
                        ) : (
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <line x1="18" y1="6" x2="6" y2="18" />
                            <line x1="6" y1="6" x2="18" y2="18" />
                          </svg>
                        )}
                      </button>
                    )}
                  </div>
                )}
              </div>

              {/* Media Info */}
              <div className="p-3">
                <p className="text-xs font-medium text-[var(--text-primary)] truncate">
                  {item.originalFileName || `Media ${item.id}`}
                </p>
                <div className="mt-1.5 flex items-center gap-2 text-[10px] text-[var(--text-muted)]">
                  <span>{formatFileSize(item.fileSize)}</span>
                  {item.width && item.height && (
                    <>
                      <span>•</span>
                      <span>{item.width}×{item.height}</span>
                    </>
                  )}
                </div>
              </div>
            </div>
          );
        })}
      </div>

      {/* Media Modal */}
      {selectedMedia && (
        <MediaModal
          media={selectedMedia}
          onClose={() => setSelectedMedia(null)}
          onNext={() => {
            const currentIndex = media.findIndex(m => m.id === selectedMedia.id);
            const nextIndex = (currentIndex + 1) % media.length;
            setSelectedMedia(media[nextIndex]);
          }}
          onPrevious={() => {
            const currentIndex = media.findIndex(m => m.id === selectedMedia.id);
            const prevIndex = currentIndex === 0 ? media.length - 1 : currentIndex - 1;
            setSelectedMedia(media[prevIndex]);
          }}
          isAdmin={isAdmin}
          onDelete={onDelete ? () => handleDelete(selectedMedia.id) : undefined}
          onSetCover={onSetCover ? () => handleSetCover(selectedMedia.id) : undefined}
        />
      )}
    </>
  );
}
