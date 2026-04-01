import Button from "./Button";

type Props = {
  currentPage: number;
  totalPages: number;
  onPageChange: (page: number) => void;
};

export default function Pagination({ currentPage, totalPages, onPageChange }: Props) {
  if (totalPages <= 1) return null;

  const pageNumbers = Array.from({ length: totalPages }, (_, i) => i + 1);

  return (
    <div className="mt-6 flex flex-wrap items-center justify-center gap-2">
      <Button
        variant="outline"
        disabled={currentPage <= 1}
        onClick={() => onPageChange(currentPage - 1)}
      >
        Prev
      </Button>
      {pageNumbers.map((page) => (
        <button
          key={page}
          className={`rounded-lg px-3 py-1 text-sm ${
            page === currentPage
              ? "bg-amber-400 text-slate-950"
              : "border border-white/15 bg-white/5 text-white hover:bg-white/10"
          }`}
          onClick={() => onPageChange(page)}
        >
          {page}
        </button>
      ))}
      <Button
        variant="outline"
        disabled={currentPage >= totalPages}
        onClick={() => onPageChange(currentPage + 1)}
      >
        Next
      </Button>
    </div>
  );
}
