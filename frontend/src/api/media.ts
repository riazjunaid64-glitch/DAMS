import { api } from "./api";
import type { ProjectMedia, UnitMedia, UploadMediaDto, UpdateMediaDto } from "../types/media";

// Project Media API
export const getProjectMedia = async (projectId: number): Promise<ProjectMedia[]> => {
  const res = await api(`/api/Project/${projectId}/media`, undefined, false);
  if (!res.ok) throw new Error("Failed to fetch project media");
  return res.json();
};

export const uploadProjectMedia = async (
  projectId: number,
  file: File,
  uploadDto?: UploadMediaDto
): Promise<ProjectMedia> => {
  const formData = new FormData();
  formData.append("file", file);
  if (uploadDto) {
    if (uploadDto.category) formData.append("category", uploadDto.category.toString());
    if (uploadDto.altText) formData.append("altText", uploadDto.altText);
    if (uploadDto.description) formData.append("description", uploadDto.description);
    if (uploadDto.isCover) formData.append("isCover", "true");
  }

  const res = await api(`/api/Project/${projectId}/media`, {
    method: "POST",
    headers: {}, // Let browser set Content-Type for FormData
    body: formData,
  });
  if (!res.ok) throw new Error("Failed to upload project media");
  return res.json();
};

export const uploadProjectMediaBulk = async (
  projectId: number,
  files: File[],
  uploadDto?: UploadMediaDto
): Promise<ProjectMedia[]> => {
  const formData = new FormData();
  files.forEach((file) => formData.append("files", file));
  if (uploadDto) {
    if (uploadDto.category) formData.append("category", uploadDto.category.toString());
    if (uploadDto.altText) formData.append("altText", uploadDto.altText);
    if (uploadDto.description) formData.append("description", uploadDto.description);
    if (uploadDto.isCover) formData.append("isCover", "true");
  }

  const res = await api(`/api/Project/${projectId}/media/bulk`, {
    method: "POST",
    headers: {},
    body: formData,
  });
  if (!res.ok) throw new Error("Failed to upload project media bulk");
  return res.json();
};

export const updateProjectMedia = async (
  projectId: number,
  mediaId: number,
  updateDto: UpdateMediaDto
): Promise<ProjectMedia> => {
  const res = await api(`/api/Project/${projectId}/media/${mediaId}`, {
    method: "PUT",
    body: JSON.stringify(updateDto),
  });
  if (!res.ok) throw new Error("Failed to update project media");
  return res.json();
};

export const deleteProjectMedia = async (projectId: number, mediaId: number): Promise<void> => {
  const res = await api(`/api/Project/${projectId}/media/${mediaId}`, {
    method: "DELETE",
  });
  if (!res.ok) throw new Error("Failed to delete project media");
};

export const reorderProjectMedia = async (projectId: number, mediaIds: number[]): Promise<void> => {
  const res = await api(`/api/Project/${projectId}/media/reorder`, {
    method: "POST",
    body: JSON.stringify(mediaIds),
  });
  if (!res.ok) throw new Error("Failed to reorder project media");
};

export const setProjectCoverMedia = async (projectId: number, mediaId: number): Promise<void> => {
  const res = await api(`/api/Project/${projectId}/media/${mediaId}/set-cover`, {
    method: "POST",
  });
  if (!res.ok) throw new Error("Failed to set project cover media");
};

// Unit Media API
export const getUnitMedia = async (unitId: number): Promise<UnitMedia[]> => {
  const res = await api(`/api/Unit/${unitId}/media`, undefined, false);
  if (!res.ok) throw new Error("Failed to fetch unit media");
  return res.json();
};

export const getUnitMediaByProject = async (projectId: number): Promise<UnitMedia[]> => {
  const res = await api(`/api/Unit/project/${projectId}/media`, undefined, false);
  if (!res.ok) throw new Error("Failed to fetch unit media by project");
  return res.json();
};

export const uploadUnitMedia = async (
  unitId: number,
  file: File,
  uploadDto?: UploadMediaDto
): Promise<UnitMedia> => {
  const formData = new FormData();
  formData.append("file", file);
  if (uploadDto) {
    if (uploadDto.category) formData.append("category", uploadDto.category.toString());
    if (uploadDto.altText) formData.append("altText", uploadDto.altText);
    if (uploadDto.description) formData.append("description", uploadDto.description);
    if (uploadDto.isCover) formData.append("isCover", "true");
  }

  const res = await api(`/api/Unit/${unitId}/media`, {
    method: "POST",
    headers: {},
    body: formData,
  });
  if (!res.ok) throw new Error("Failed to upload unit media");
  return res.json();
};

export const uploadUnitMediaBulk = async (
  unitId: number,
  files: File[],
  uploadDto?: UploadMediaDto
): Promise<UnitMedia[]> => {
  const formData = new FormData();
  files.forEach((file) => formData.append("files", file));
  if (uploadDto) {
    if (uploadDto.category) formData.append("category", uploadDto.category.toString());
    if (uploadDto.altText) formData.append("altText", uploadDto.altText);
    if (uploadDto.description) formData.append("description", uploadDto.description);
    if (uploadDto.isCover) formData.append("isCover", "true");
  }

  const res = await api(`/api/Unit/${unitId}/media/bulk`, {
    method: "POST",
    headers: {},
    body: formData,
  });
  if (!res.ok) throw new Error("Failed to upload unit media bulk");
  return res.json();
};

export const updateUnitMedia = async (
  unitId: number,
  mediaId: number,
  updateDto: UpdateMediaDto
): Promise<UnitMedia> => {
  const res = await api(`/api/Unit/${unitId}/media/${mediaId}`, {
    method: "PUT",
    body: JSON.stringify(updateDto),
  });
  if (!res.ok) throw new Error("Failed to update unit media");
  return res.json();
};

export const deleteUnitMedia = async (unitId: number, mediaId: number): Promise<void> => {
  const res = await api(`/api/Unit/${unitId}/media/${mediaId}`, {
    method: "DELETE",
  });
  if (!res.ok) throw new Error("Failed to delete unit media");
};

export const reorderUnitMedia = async (unitId: number, mediaIds: number[]): Promise<void> => {
  const res = await api(`/api/Unit/${unitId}/media/reorder`, {
    method: "POST",
    body: JSON.stringify(mediaIds),
  });
  if (!res.ok) throw new Error("Failed to reorder unit media");
};

export const setUnitCoverMedia = async (unitId: number, mediaId: number): Promise<void> => {
  const res = await api(`/api/Unit/${unitId}/media/${mediaId}/set-cover`, {
    method: "POST",
  });
  if (!res.ok) throw new Error("Failed to set unit cover media");
};
