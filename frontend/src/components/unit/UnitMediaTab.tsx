import { useState } from "react";
import type { User } from "../../App.tsx";
import type { UnitMedia, UploadMediaDto } from "../../types/media.ts";
import { uploadUnitMediaBulk, deleteUnitMedia, setUnitCoverMedia } from "../../api/media.ts";
import Button from "../../lib/Button.tsx";
import MediaUpload from "../MediaUpload.tsx";
import MediaGallery from "../MediaGallery.tsx";

interface Props {
  unitId: number;
  media: UnitMedia[];
  onMediaChange: (media: UnitMedia[]) => void;
  user: User | null;
  loading: boolean;
}

export default function UnitMediaTab({ unitId, media, onMediaChange, user, loading }: Props) {
  const isAdmin = user?.role === "Admin";
  const [showUpload, setShowUpload] = useState(false);
  const [uploading, setUploading] = useState(false);

  const handleUpload = async (files: File[], uploadDto?: UploadMediaDto) => {
    setUploading(true);
    try {
      const uploaded = await uploadUnitMediaBulk(unitId, files, uploadDto);
      onMediaChange([...media, ...uploaded]);
      setShowUpload(false);
    } catch (error) {
      console.error("Upload failed:", error);
      alert("Failed to upload media. Please try again.");
    } finally {
      setUploading(false);
    }
  };

  const handleDelete = async (mediaId: number) => {
    try {
      await deleteUnitMedia(unitId, mediaId);
      onMediaChange(media.filter((m) => m.id !== mediaId));
    } catch (error) {
      console.error("Delete failed:", error);
      alert("Failed to delete media. Please try again.");
    }
  };

  const handleSetCover = async (mediaId: number) => {
    try {
      await setUnitCoverMedia(unitId, mediaId);
      onMediaChange(media.map((m) => ({ ...m, isCover: m.id === mediaId })));
    } catch (error) {
      console.error("Set cover failed:", error);
      alert("Failed to set cover. Please try again.");
    }
  };

  return (
    <div className="py-8 sm:py-10">
      <div className="mx-auto w-full max-w-7xl px-5 sm:px-8 lg:px-10">
        {/* Header */}
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between mb-6">
          <div>
            <h2 className="section-title" style={{ marginBottom: 0 }}>Unit Media</h2>
            <p className="text-sm text-[var(--text-muted)] mt-1">
              {media.length} media file{media.length !== 1 ? "s" : ""}
            </p>
          </div>
          {isAdmin && (
            <Button size="sm" variant="outline" onClick={() => setShowUpload((p) => !p)}>
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                <polyline points="17 8 12 3 7 8" />
                <line x1="12" y1="3" x2="12" y2="15" />
              </svg>
              {showUpload ? "Cancel" : "Upload Media"}
            </Button>
          )}
        </div>

        {/* Upload Area */}
        {showUpload && isAdmin && (
          <div className="mb-8 animate-scale-in rounded-2xl border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] p-6 shadow-sm">
            <h4 className="mb-4 text-sm font-semibold text-[var(--text-heading)]">Upload Unit Media</h4>
            <MediaUpload onUpload={handleUpload} multiple={true} uploading={uploading} />
          </div>
        )}

        {/* Gallery */}
        <MediaGallery
          media={media}
          onDelete={isAdmin ? handleDelete : undefined}
          onSetCover={isAdmin ? handleSetCover : undefined}
          isAdmin={isAdmin}
          loading={loading}
        />
      </div>
    </div>
  );
}
