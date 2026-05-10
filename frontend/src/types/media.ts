export const MediaCategory = {
  Gallery: 1,
  Thumbnail: 2,
  FloorPlan: 3,
  Brochure: 4,
  ConstructionProgress: 5,
  Interior: 6,
  Exterior: 7,
  Document: 8,
  Video: 9,
} as const;

export type MediaCategory = typeof MediaCategory[keyof typeof MediaCategory];

export interface ProjectMedia {
  id: number;
  projectId: number;
  mediaUrl: string;
  mediaType: string;
  uploadedAt: string;
  category: MediaCategory;
  isCover: boolean;
  displayOrder: number;
  altText?: string | null;
  description?: string | null;
  fileSize: number;
  width?: number | null;
  height?: number | null;
  originalFileName?: string | null;
  mimeType?: string | null;
  updatedAt?: string | null;
}

export interface UnitMedia {
  id: number;
  unitId: number;
  mediaUrl: string;
  mediaType: string;
  uploadedAt: string;
  category: MediaCategory;
  isCover: boolean;
  displayOrder: number;
  altText?: string | null;
  description?: string | null;
  fileSize: number;
  width?: number | null;
  height?: number | null;
  originalFileName?: string | null;
  mimeType?: string | null;
  updatedAt?: string | null;
}

export interface UploadMediaDto {
  category?: MediaCategory;
  altText?: string;
  description?: string;
  isCover?: boolean;
}

export interface UpdateMediaDto {
  category?: MediaCategory;
  isCover?: boolean;
  displayOrder?: number;
  altText?: string;
  description?: string;
}
