import { useState } from "react";
import { ActionsMenu } from "./ActionsMenu.tsx";
import { cx } from "./cx.ts";
import { IconClose, IconImage, IconStar, IconTrash, IconUpload } from "./icons.tsx";
import { ConfirmDialog } from "./Modal.tsx";
import { Overlay } from "./Overlay.tsx";
import { PhotoSlider, type Photo } from "./PhotoSlider.tsx";

export type PhotoGalleryProps = {
  photos: readonly Photo[];
  /** Admins: shows the ⋯ menu on each photo and the Upload tile. */
  canManage?: boolean;
  onSetCover?: (photo: Photo) => void;
  /** Called after the user confirms. */
  onDelete?: (photo: Photo) => void;
  onUpload?: (files: File[]) => void;
  uploading?: boolean;
  className?: string;
};

const tileClass = "relative aspect-[4/3] overflow-hidden rounded-menu";

/**
 * Photo grid (2 columns on phone, 4 on desktop) with the Cover tag, a ⋯ menu per photo for admins
 * (Set as cover / Delete photo) and an Upload tile. Tapping a photo opens it full size.
 */
export function PhotoGallery({ photos, canManage = false, onSetCover, onDelete, onUpload, uploading = false, className }: PhotoGalleryProps) {
  const [viewing, setViewing] = useState<number | null>(null);
  const [deleting, setDeleting] = useState<Photo | null>(null);

  return (
    <div className={cx("grid grid-cols-2 gap-3 font-ui md:grid-cols-4", className)}>
      {photos.map((photo, index) => (
        <div key={photo.id} className={cx(tileClass, "bg-track")}>
          <button
            type="button"
            aria-label={`Open photo ${index + 1}`}
            onClick={() => setViewing(index)}
            className="block size-full cursor-pointer focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary"
          >
            {photo.src
              ? <img src={photo.src} alt={photo.alt ?? ""} loading="lazy" className="size-full object-cover" />
              : <span className="flex size-full items-center justify-center text-ink-faint"><IconImage size={26} /></span>}
          </button>
          {photo.isCover && (
            <span className="pointer-events-none absolute top-2 left-2 rounded-full bg-gold px-2.5 py-0.5 text-caption font-extrabold text-ink">Cover</span>
          )}
          {canManage && (onSetCover || onDelete) && (
            <ActionsMenu
              trigger="dots"
              aria-label={`Photo ${index + 1} actions`}
              className="absolute top-2 right-2"
              items={[
                ...(onSetCover && !photo.isCover ? [{ label: "Set as cover", icon: <IconStar size={16} />, onSelect: () => onSetCover(photo) }] : []),
                ...(onDelete ? [{ label: "Delete photo", icon: <IconTrash size={16} />, danger: true, onSelect: () => setDeleting(photo) }] : []),
              ]}
            />
          )}
        </div>
      ))}
      {canManage && onUpload && (
        <label
          className={cx(
            tileClass,
            "flex cursor-pointer flex-col items-center justify-center gap-1.5 border-[1.5px] border-dashed border-line-input text-small font-bold text-primary hover:bg-card",
            "has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-primary",
            uploading && "pointer-events-none opacity-60",
          )}
        >
          <IconUpload size={16} />
          {uploading ? "Uploading…" : "Upload photos"}
          <input
            type="file"
            accept="image/*"
            multiple
            className="sr-only"
            disabled={uploading}
            onChange={(event) => {
              const files = Array.from(event.target.files ?? []);
              event.target.value = "";
              if (files.length) onUpload(files);
            }}
          />
        </label>
      )}

      <Overlay open={viewing !== null} onClose={() => setViewing(null)}>
        <div role="dialog" aria-modal="true" aria-label="Photo" className="relative z-10 w-full max-w-5xl">
          <button
            type="button"
            aria-label="Close"
            onClick={() => setViewing(null)}
            className="absolute -top-13 right-0 flex size-11 cursor-pointer items-center justify-center rounded-full bg-card text-ink focus-visible:outline-2 focus-visible:outline-primary"
          >
            <IconClose size={20} />
          </button>
          {viewing !== null && <PhotoSlider photos={photos} initialIndex={viewing} fit="contain" className="aspect-[4/3] bg-ink md:aspect-[16/10]" />}
        </div>
      </Overlay>

      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        onConfirm={() => { if (deleting) onDelete?.(deleting); setDeleting(null); }}
        title="Delete this photo?"
        message="This photo will be removed. This cannot be undone."
        confirmLabel="Delete photo"
        danger
      />
    </div>
  );
}
