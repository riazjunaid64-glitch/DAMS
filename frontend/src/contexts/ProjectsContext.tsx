import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { api } from "../api/api";
import { parseProjectsPayload, type ProjectFromApi } from "../utils/parseProject";

interface ProjectsContextValue {
  projects: ProjectFromApi[];
  loading: boolean;
  error: string | null;
  reload: () => Promise<void>;
}

const ProjectsContext = createContext<ProjectsContextValue>({
  projects: [],
  loading: true,
  error: null,
  reload: async () => {},
});

export function ProjectsProvider({ children }: { children: ReactNode }) {
  const [projects, setProjects] = useState<ProjectFromApi[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const didFetch = useRef(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api("/api/Project", undefined, false);
      if (!res.ok) {
        setError(`Unable to load projects (HTTP ${res.status}).`);
        return;
      }
      const raw: unknown = await res.json();
      setProjects(Array.isArray(raw) ? parseProjectsPayload(raw) : []);
    } catch {
      setError("Unable to load projects right now.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (didFetch.current) return;
    didFetch.current = true;
    void load();
  }, [load]);

  // Stable value identity so consumers don't re-render on unrelated App re-renders.
  const value = useMemo(
    () => ({ projects, loading, error, reload: load }),
    [projects, loading, error, load],
  );

  return <ProjectsContext.Provider value={value}>{children}</ProjectsContext.Provider>;
}

export const useProjects = () => useContext(ProjectsContext);
