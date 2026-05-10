import { useState, useRef, useCallback } from "react";
import type { UploadMediaDto, MediaCategory } from "../types/media.ts";

interface MediaUploadProps {
  onUpload: (files: File[], uploadDto?: UploadMediaDto) => Promise<void>;
  multiple?: boolean;
  accept?: string;
  maxSize?: number; // in bytes
  disabled?: boolean;
  uploading?: boolean;
}

export default function MediaUpload({
  onUpload,
  multiple = false,
  accept = "image/*,video/*,.pdf",
  maxSize = 50 * 1024 * 1024, // 50MB
  disabled = false,
  uploading = false,
}: MediaUploadProps) {
  const [isDragging, setIsDragging] = useState(false);
  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const [uploadDto, setUploadDto] = useState<UploadMediaDto>({
    category: 1, // Gallery
    isCover: false,
    altText: "",
    description: "",
  });
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (!disabled && !uploading) {
      setIsDragging(true);
    }
  }, [disabled, uploading]);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);

    if (disabled || uploading) return;

    const files = Array.from(e.dataTransfer.files);
    handleFiles(files);
  }, [disabled, uploading]);

  const handleFileSelect = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files || []);
    handleFiles(files);
  }, []);

  const handleFiles = (files: File[]) => {
    const validFiles = files.filter((file) => {
      if (file.size > maxSize) {
        alert(`File "${file.name}" exceeds maximum size of ${maxSize / 1024 / 1024}MB`);
        return false;
      }
      return true;
    });

    if (validFiles.length > 0) {
      setSelectedFiles((prev) => (multiple ? [...prev, ...validFiles] : validFiles));
    }
  };

  const removeFile = (index: number) => {
    setSelectedFiles((prev) => prev.filter((_, i) => i !== index));
  };

  const handleUpload = async () => {
    if (selectedFiles.length === 0) return;
    
    try {
      await onUpload(selectedFiles, uploadDto);
      setSelectedFiles([]);
      setUploadDto({
        category: 1,
        isCover: false,
        altText: "",
        description: "",
      });
      if (fileInputRef.current) {
        fileInputRef.current.value = "";
      }
    } catch (error) {
      console.error("Upload failed:", error);
      alert("Failed to upload files. Please try again.");
    }
  };

  const formatFileSize = (bytes: number) => {
    if (bytes === 0) return "0 Bytes";
    const k = 1024;
    const sizes = ["Bytes", "KB", "MB", "GB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round(bytes / Math.pow(k, i) * 100) / 100 + " " + sizes[i];
  };

  return (
    <div className="space-y-4">
      {/* Upload Area */}
      <div
        className={`relative rounded-2xl border-2 border-dashed p-8 text-center transition-all duration-200 ${
          isDragging
            ? "border-[var(--accent)] bg-[var(--accent-glow)]"
            : "border-[var(--border)] bg-[var(--surface-glass)] hover:border-[var(--border-hover)] hover:bg-[var(--surface-glass-hover)]"
        } ${disabled || uploading ? "opacity-50 cursor-not-allowed" : "cursor-pointer"}`}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
        onClick={() => !disabled && !uploading && fileInputRef.current?.click()}
      >
        <input
          ref={fileInputRef}
          type="file"
          multiple={multiple}
          accept={accept}
          onChange={handleFileSelect}
          className="hidden"
          disabled={disabled || uploading}
        />
        
        <div className="flex flex-col items-center gap-3">
          <div className={`flex h-12 w-12 items-center justify-center rounded-xl ${
            isDragging ? "bg-[var(--accent)]" : "bg-[var(--surface-glass-hover)]"
          }`}>
            <svg
              width="24"
              height="24"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              className={isDragging ? "text-white" : "text-[var(--text-muted)]"}
            >
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
              <polyline points="17 8 12 3 7 8" />
              <line x1="12" y1="3" x2="12" y2="15" />
            </svg>
          </div>
          
          <div className="space-y-1">
            <p className="text-sm font-medium text-[var(--text-primary)]">
              {uploading ? "Uploading..." : "Drop files here or click to browse"}
            </p>
            <p className="text-xs text-[var(--text-muted)]">
              {multiple ? "Upload multiple files" : "Upload single file"} • Max {maxSize / 1024 / 1024}MB
            </p>
          </div>
        </div>
      </div>

      {/* Selected Files Preview */}
      {selectedFiles.length > 0 && (
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <h4 className="text-sm font-medium text-[var(--text-secondary)]">
              Selected Files ({selectedFiles.length})
            </h4>
            <button
              onClick={() => setSelectedFiles([])}
              className="text-xs text-[var(--text-muted)] hover:text-[var(--text-primary)] transition-colors"
            >
              Clear All
            </button>
          </div>

          <div className="space-y-2">
            {selectedFiles.map((file, index) => (
              <div
                key={`${file.name}-${index}`}
                className="flex items-center gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-3"
              >
                <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-[var(--surface-glass-hover)]">
                  {file.type.startsWith("image/") ? (
                    <svg
                      width="18"
                      height="18"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      className="text-[var(--text-muted)]"
                    >
                      <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
                      <circle cx="8.5" cy="8.5" r="1.5" />
                      <polyline points="21 15 16 10 5 21" />
                    </svg>
                  ) : file.type.startsWith("video/") ? (
                    <svg
                      width="18"
                      height="18"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      className="text-[var(--text-muted)]"
                    >
                      <polygon points="23 7 16 12 23 17 23 7" />
                      <rect x="1" y="5" width="15" height="14" rx="2" ry="2" />
                    </svg>
                  ) : (
                    <svg
                      width="18"
                      height="18"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      className="text-[var(--text-muted)]"
                    >
                      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                      <polyline points="14 2 14 8 20 8" />
                      <line x1="16" y1="13" x2="8" y2="13" />
                      <line x1="16" y1="17" x2="8" y2="17" />
                      <polyline points="10 9 9 9 8 9" />
                    </svg>
                  )}
                </div>

                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium text-[var(--text-primary)] truncate">
                    {file.name}
                  </p>
                  <p className="text-xs text-[var(--text-muted)]">
                    {formatFileSize(file.size)}
                  </p>
                </div>

                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    removeFile(index);
                  }}
                  className="flex h-8 w-8 items-center justify-center rounded-lg text-[var(--text-muted)] transition-colors hover:bg-[var(--surface-glass-hover)] hover:text-[var(--text-primary)]"
                >
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                    <line x1="18" y1="6" x2="6" y2="18" />
                    <line x1="6" y1="6" x2="18" y2="18" />
                  </svg>
                </button>
              </div>
            ))}
          </div>

          {/* Upload Options */}
          <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4 space-y-3">
            <div>
              <label className="text-xs font-medium text-[var(--text-secondary)] mb-2 block">
                Category
              </label>
              <select
                value={uploadDto.category}
                onChange={(e) => setUploadDto({ ...uploadDto, category: Number(e.target.value) as MediaCategory })}
                className="w-full rounded-lg border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
              >
                <option value={1}>Gallery</option>
                <option value={2}>Thumbnail</option>
                <option value={3}>Floor Plan</option>
                <option value={4}>Brochure</option>
                <option value={5}>Construction Progress</option>
                <option value={6}>Interior</option>
                <option value={7}>Exterior</option>
                <option value={8}>Document</option>
                <option value={9}>Video</option>
              </select>
            </div>

            <div className="flex items-center gap-2">
              <input
                type="checkbox"
                id="isCover"
                checked={uploadDto.isCover}
                onChange={(e) => setUploadDto({ ...uploadDto, isCover: e.target.checked })}
                className="h-4 w-4 rounded border-[var(--border)] bg-[var(--input-bg)] text-[var(--accent)] focus:ring-[var(--accent-glow)]"
              />
              <label htmlFor="isCover" className="text-sm text-[var(--text-secondary)]">
                Set as cover image
              </label>
            </div>

            <div>
              <label className="text-xs font-medium text-[var(--text-secondary)] mb-2 block">
                Alt Text (optional)
              </label>
              <input
                type="text"
                value={uploadDto.altText || ""}
                onChange={(e) => setUploadDto({ ...uploadDto, altText: e.target.value })}
                placeholder="Describe this image for accessibility"
                className="w-full rounded-lg border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] placeholder:text-[var(--text-muted)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
              />
            </div>

            <div>
              <label className="text-xs font-medium text-[var(--text-secondary)] mb-2 block">
                Description (optional)
              </label>
              <textarea
                value={uploadDto.description || ""}
                onChange={(e) => setUploadDto({ ...uploadDto, description: e.target.value })}
                placeholder="Add a description..."
                rows={2}
                className="w-full rounded-lg border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] placeholder:text-[var(--text-muted)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)] resize-none"
              />
            </div>
          </div>

          {/* Upload Button */}
          <button
            onClick={handleUpload}
            disabled={uploading || selectedFiles.length === 0}
            className="w-full rounded-xl bg-gradient-to-r from-indigo-500 to-violet-600 px-4 py-3 text-sm font-semibold text-white shadow-md transition-all duration-200 hover:shadow-lg hover:from-indigo-600 hover:to-violet-700 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {uploading ? "Uploading..." : `Upload ${selectedFiles.length} file${selectedFiles.length > 1 ? "s" : ""}`}
          </button>
        </div>
      )}
    </div>
  );
}
