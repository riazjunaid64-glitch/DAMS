export type FeaturedProject = {
  id: number;
  title: string;
  badge: string;
  category: "residential" | "commercial" | "mixed";
  location: string;
  status: string;
  description: string;
  image: string;
};

export const FEATURED_PROJECTS: FeaturedProject[] = [
  {
    id: 1,
    title: "CPEC Greens",
    category: "residential",
    badge: "Residential",
    location: "Multi Gardens B-17 (CPEC), Islamabad",
    status: "Available",
    description: "Premium living and investment project near Multi Gardens B-17 Islamabad.",
    image: "/images/projects/cpec-greens-sm.webp",
  },
  {
    id: 2,
    title: "Deen Square",
    category: "mixed",
    badge: "Mixed Use",
    location: "Multi Gardens B-17, Islamabad",
    status: "Available",
    description: "A mixed-use project including residential and commercial options in B-17.",
    image: "/images/projects/deen-square-sm.webp",
  },
  {
    id: 3,
    title: "Mall of Faisal Hills",
    category: "commercial",
    badge: "Commercial",
    location: "Executive Block, Faisal Hills (Main Boulevard), GT Road N-5 Taxila",
    status: "Available",
    description: "A landmark commercial mall project at Faisal Hills Executive Block.",
    image: "/images/projects/mall-of-faisal-hills-sm.webp",
  },
  {
    id: 4,
    title: "Urban Complex",
    category: "mixed",
    badge: "Mixed Use",
    location: "Corner Block C, Main Boulevard, Faisal Hills (N-5), Taxila",
    status: "Available",
    description: "Urban lifestyle complex on Main Boulevard Faisal Hills corner location.",
    image: "/images/projects/urban-complex-sm.webp",
  },
  {
    id: 5,
    title: "Legacy Court",
    category: "residential",
    badge: "Residential",
    location: "Faisal Hills, Islamabad (GT Road N-5), Taxila",
    status: "Available",
    description: "A residential-focused court-style project planned at Faisal Hills.",
    image: "/images/projects/legacy-court-sm.webp",
  },
  {
    id: 6,
    title: "Seventeen Square",
    category: "commercial",
    badge: "Commercial",
    location: "Plot #2, Block A, Main Markaz, Sector B-17, Islamabad",
    status: "Available",
    description: "Prime project at Main Markaz B-17 Islamabad with strong market access.",
    image: "/images/projects/seventeen-square-sm.webp",
  },
];
