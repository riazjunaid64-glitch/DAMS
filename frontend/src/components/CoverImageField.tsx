import { useEffect, useMemo, useRef, type ChangeEvent } from "react";

type CoverImageFieldProps = {
  label?: string;
  file: File | null;
  onChange: (file: File | null) => void;
  existingPreviewUrl?: string | null;
  hint?: string;
};

const ACCEPT = "image/jpeg,image/png,image/webp,image/gif";

export default function CoverImageField({
  label = "Cover Image",
  file,
  onChange,
  existingPreviewUrl,
  hint = "Optional — shown on project cards",
}: CoverImageFieldProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const objectUrl = useMemo(() => file ? URL.createObjectURL(file) : null, [file]);
  const previewUrl = objectUrl ?? existingPreviewUrl ?? null;

  useEffect(() => {
    return () => {
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [objectUrl]);

  const handleFileChange = (event: ChangeEvent<HTMLInputElement>) => {
    const selected = event.target.files?.[0] ?? null;
    onChange(selected);
  };

  const clearImage = () => {
    onChange(null);
    if (inputRef.current) inputRef.current.value = "";
  };

  return (
    <div className="cover-image-field">
      <div className="cover-image-field__head">
        <span className="cover-image-field__label">{label}</span>
        {hint && <span className="cover-image-field__hint">{hint}</span>}
      </div>

      <div className="cover-image-field__body">
        <div className="cover-image-field__preview">
          {previewUrl ? (
            <img src={previewUrl} alt="Cover preview" className="cover-image-field__img" />
          ) : (
            <div className="cover-image-field__placeholder">
              <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
                <rect x="3" y="3" width="18" height="18" rx="2" />
                <circle cx="8.5" cy="8.5" r="1.5" />
                <path d="M21 15l-5-5L5 21" />
              </svg>
              <span>Upload a project photo</span>
            </div>
          )}
        </div>

        <div className="cover-image-field__actions">
          <button
            type="button"
            className="cover-image-field__btn"
            onClick={() => inputRef.current?.click()}
          >
            {previewUrl ? "Change image" : "Choose image"}
          </button>
          {previewUrl && (
            <button type="button" className="cover-image-field__btn cover-image-field__btn--ghost" onClick={clearImage}>
              Remove
            </button>
          )}
          <input
            ref={inputRef}
            type="file"
            accept={ACCEPT}
            className="sr-only"
            onChange={handleFileChange}
          />
        </div>
      </div>
    </div>
  );
}
