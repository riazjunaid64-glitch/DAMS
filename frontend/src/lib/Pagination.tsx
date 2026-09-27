import { Pagination as SharedPagination } from "../components/ui";

type Props = {
  currentPage: number;
  totalPages: number;
  onPageChange: (page: number) => void;
};

/** The page-count API existing screens use, drawn by the shared Pagination. */
export default function Pagination({ currentPage, totalPages, onPageChange }: Props) {
  if (totalPages <= 1) return null;
  return <SharedPagination page={currentPage} totalPages={totalPages} onPageChange={onPageChange} className="mt-6" />;
}
