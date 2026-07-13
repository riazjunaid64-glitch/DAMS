import { createContext, useContext } from "react";
import type { ProjectFromApi } from "../utils/parseProject";

export interface ProjectsContextValue {
  projects: ProjectFromApi[];
  loading: boolean;
  error: string | null;
  reload: () => Promise<void>;
}

export const ProjectsContext = createContext<ProjectsContextValue>({
  projects: [],
  loading: true,
  error: null,
  reload: async () => {},
});

export const useProjects = () => useContext(ProjectsContext);
