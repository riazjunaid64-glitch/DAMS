import { useEffect, useState, type ReactNode } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import type { User } from "../App.tsx";
import {
  Button,
  DataTable,
  EmptyState,
  FilterBar,
  IconAlert,
  IconPlus,
  IconSearch,
  IconSettings,
  IconUsers,
  LoadMore,
  Modal,
  PageHeader,
  Pagination,
  Spinner,
  StatSummary,
  StatusBadge,
  cx,
  useIsPhone,
  useToast,
  type DataTableColumn,
  type FilterValues,
} from "../components/ui";
import { DEFAULT_PAGE_SIZE } from "../components/ui/pageItems.ts";
import { can } from "../features/access/permissions.ts";
import { formatPhone } from "../features/bookings/format.ts";
import { CustomerForm, duplicateFieldError, type CustomerFormErrors } from "../features/customers/CustomerForm.tsx";
import {
  customerFormErrors,
  emptyCustomerForm,
  type CustomerFormValues,
} from "../features/customers/customerForm.ts";
import { apiJson } from "../features/leads/leadApi.ts";
import { api } from "../api/api.ts";

type Props = { user: User | null };

interface CustomerRow {
  id: number;
  fullName: string;
  phone: string;
  cnic?: string | null;
  isBlocked: boolean;
  bookingsCount: number;
  documentsNeeded: number;
}

interface CustomerList {
  items: CustomerRow[];
  totalCount: number;
  totalCustomers: number;
  documentsNeededCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

const FILTER_KEYS = ["search", "documentsNeededOnly"] as const;

type Loaded<T> = { query: string; data: T };

type ConflictBody = {
  message?: string;
  field?: string;
  existingCustomerId?: number;
  existingCustomerName?: string;
};

export default function CustomersPage({ user }: Props) {
  if (!user || !can(user.role, "customers")) {
    return (
      <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:px-8 md:py-7">
        <EmptyState
          icon={<IconUsers size={26} />}
          title={user ? "You don't have access to Customers." : "Sign in required"}
          message={user ? undefined : "Sign in with a staff account to open Customers."}
        />
      </div>
    );
  }
  return <CustomersWorkspace />;
}

function CustomersWorkspace() {
  const navigate = useNavigate();
  const toast = useToast();
  const isPhone = useIsPhone();
  const [params, setParams] = useSearchParams();

  const pageParam = Number(params.get("page") ?? 1);
  const page = Number.isInteger(pageParam) && pageParam > 0 ? pageParam : 1;
  const docsOnly = params.get("documentsNeededOnly") === "1";

  const summaryParams = new URLSearchParams();
  const search = params.get("search");
  if (search) summaryParams.set("search", search);
  const listParams = new URLSearchParams(summaryParams);
  if (docsOnly) listParams.set("documentsNeededOnly", "true");
  listParams.set("page", String(page));
  listParams.set("pageSize", String(DEFAULT_PAGE_SIZE));
  const listQuery = listParams.toString();

  const [list, setList] = useState<Loaded<CustomerList> | null>(null);
  const [failure, setFailure] = useState<Loaded<string> | null>(null);
  const [refresh, setRefresh] = useState(0);
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    apiJson<CustomerList>(`/api/Customer?${listQuery}`, { signal: controller.signal })
      .then((data) => {
        setList({ query: listQuery, data });
        setFailure(null);
      })
      .catch((caught: unknown) => {
        if (controller.signal.aborted) return;
        setFailure({
          query: listQuery,
          data: caught instanceof Error ? caught.message : "Customers could not be loaded.",
        });
      });
    return () => controller.abort();
  }, [listQuery, refresh]);

  const [more, setMore] = useState<{ query: string; items: CustomerRow[]; page: number; loading: boolean } | null>(null);
  const extra = more?.query === listQuery ? more : null;
  const current = list?.query === listQuery ? list.data : null;

  const loadMore = async () => {
    if (!current) return;
    const nextPage = (extra?.page ?? current.page) + 1;
    const next = new URLSearchParams(listQuery);
    next.set("page", String(nextPage));
    setMore({ query: listQuery, items: extra?.items ?? [], page: extra?.page ?? current.page, loading: true });
    try {
      const data = await apiJson<CustomerList>(`/api/Customer?${next.toString()}`);
      setMore((shown) =>
        shown?.query === listQuery
          ? { query: listQuery, items: [...shown.items, ...data.items], page: nextPage, loading: false }
          : shown,
      );
    } catch (caught) {
      setMore((shown) => shown && { ...shown, loading: false });
      toast.error(caught instanceof Error ? caught.message : "More customers could not be loaded.");
    }
  };

  const updateParams = (changes: FilterValues) => {
    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value);
      else next.delete(key);
    }
    if (!("page" in changes)) next.delete("page");
    setParams(next);
  };

  const hasFilters = FILTER_KEYS.some((key) => params.get(key));
  const resetFilters = () => updateParams(Object.fromEntries(FILTER_KEYS.map((key) => [key, ""])));

  const count = (value?: number) => value?.toLocaleString("en-PK") ?? "—";
  const totals = current ?? list?.data;
  const shown = current ? [...current.items, ...(isPhone && extra ? extra.items : [])] : [];
  const rows = current ? shown : list?.data.items ?? [];
  const failed = failure?.query === listQuery ? failure.data : null;
  const loading = !current && !failed;
  const openCustomer = (customer: CustomerRow) => navigate(`/customers/${customer.id}`);
  const retry = () => {
    setFailure(null);
    setRefresh((n) => n + 1);
  };

  const filterValues: FilterValues = {
    search: params.get("search") ?? "",
    documentsNeededOnly: docsOnly ? "1" : "",
  };

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7">
      <PageHeader
        title="Customers"
        actions={
          isPhone ? (
            <>
              <Button
                variant="outline"
                iconOnly
                icon={<IconSettings size={18} />}
                aria-label="Document setup"
                onClick={() => navigate("/customer-document-categories")}
              />
              <Button
                iconOnly
                icon={<IconPlus size={18} />}
                aria-label="New customer"
                onClick={() => setCreating(true)}
              />
            </>
          ) : (
            <>
              <Button
                variant="outline"
                icon={<IconSettings size={16} />}
                onClick={() => navigate("/customer-document-categories")}
              >
                Document setup
              </Button>
              <Button icon={<IconPlus size={16} />} onClick={() => setCreating(true)}>
                New customer
              </Button>
            </>
          )
        }
        className="max-md:flex-row max-md:items-center max-md:justify-between"
      />

      <StatSummary
        total={{
          label: "Total customers",
          value: count(totals?.totalCustomers),
          selected: !docsOnly,
          onClick: () => updateParams({ documentsNeededOnly: "" }),
        }}
        items={[
          {
            label: "Documents needed",
            value: count(totals?.documentsNeededCount),
            tone: "orange",
            selected: docsOnly,
            onClick: () => updateParams({ documentsNeededOnly: docsOnly ? "" : "1" }),
          },
        ]}
      />

      <FilterBar
        search={{
          value: params.get("search") ?? "",
          onSearch: (value) => updateParams({ search: value }),
          placeholder: "Name, phone or CNIC",
        }}
        filters={[]}
        values={filterValues}
        onChange={updateParams}
        onReset={resetFilters}
      />

      {failed && !rows.length ? (
        <EmptyState
          icon={
            <span className="flex size-12 items-center justify-center rounded-field bg-danger-soft text-danger">
              <IconAlert size={24} />
            </span>
          }
          title="Customers could not be loaded"
          message={failed}
          action={
            <Button variant="outline" onClick={retry}>
              Try again
            </Button>
          }
        />
      ) : !list ? (
        <div className="flex flex-col gap-2.5">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-16 animate-pulse rounded-card bg-track" />
          ))}
        </div>
      ) : current && current.totalCount === 0 ? (
        <EmptyState
          icon={
            <span className="flex size-12 items-center justify-center rounded-field bg-selected text-ink">
              <IconSearch size={22} />
            </span>
          }
          title={hasFilters ? "No customers match this search" : "No customers yet"}
          message={hasFilters ? undefined : "Add a customer to get started."}
          action={
            hasFilters && isPhone ? (
              <Button variant="outline" onClick={resetFilters}>
                Clear search
              </Button>
            ) : undefined
          }
        />
      ) : (
        <>
          <div aria-busy={loading || undefined} className={cx("relative transition-opacity", (loading || failed) && "opacity-60")}>
            {loading && <Spinner className="absolute top-3 right-3 z-10 text-primary" />}
            {failed && (
              <div
                role="alert"
                className="mb-2.5 flex items-center justify-between gap-3 rounded-card border border-danger-line bg-danger-soft px-4 py-2.5 text-small font-bold text-danger"
              >
                {failed}
                <Button variant="outline" size="sm" onClick={retry}>
                  Try again
                </Button>
              </div>
            )}
            <DataTable
              caption="Customers"
              rows={rows}
              rowKey={(customer) => customer.id}
              columns={customerColumns(openCustomer)}
              onRowClick={openCustomer}
              rowLabel={(customer) => `Open ${customer.fullName}`}
              minWidth={900}
              phoneCard={(customer) => <CustomerCard customer={customer} onOpen={() => openCustomer(customer)} />}
            />
          </div>
          {current &&
            (isPhone ? (
              <LoadMore
                shown={(current.page - 1) * current.pageSize + shown.length}
                total={current.totalCount}
                loading={extra?.loading ?? false}
                onLoadMore={() => void loadMore()}
              />
            ) : (
              <Pagination
                page={current.page}
                pageSize={current.pageSize}
                totalCount={current.totalCount}
                totalPages={current.totalPages}
                itemLabel="customers"
                onPageChange={(next) => updateParams({ page: String(next) })}
              />
            ))}
        </>
      )}

      <NewCustomerModal
        open={creating}
        onClose={() => setCreating(false)}
        onSaved={(id) => {
          setCreating(false);
          toast.success("Customer saved");
          navigate(`/customers/${id}`);
        }}
      />
    </div>
  );
}

function documentsBadge(needed: number): ReactNode {
  if (needed > 0) {
    return (
      <StatusBadge status="Needed" tone="orange">
        {needed} needed
      </StatusBadge>
    );
  }
  return (
    <StatusBadge status="Uploaded" tone="green">
      Complete
    </StatusBadge>
  );
}

function formatCnicDisplay(cnic: string | null | undefined): ReactNode {
  if (!cnic?.trim()) return <span className="text-ink-faint">Not added</span>;
  return cnic.trim();
}

function customerColumns(open: (customer: CustomerRow) => void): DataTableColumn<CustomerRow>[] {
  return [
    {
      key: "customer",
      header: "Customer",
      render: (customer) => (
        <>
          <span className="flex flex-wrap items-center gap-2">
            <span className="font-extrabold text-ink">{customer.fullName}</span>
            {customer.isBlocked && <StatusBadge status="Blocked" />}
          </span>
          <span className="block text-small text-ink-muted">{formatPhone(customer.phone)}</span>
        </>
      ),
    },
    {
      key: "cnic",
      header: "CNIC",
      render: (customer) => formatCnicDisplay(customer.cnic),
    },
    {
      key: "bookings",
      header: "Bookings",
      render: (customer) => customer.bookingsCount.toLocaleString("en-PK"),
    },
    {
      key: "documents",
      header: "Documents",
      render: (customer) => documentsBadge(customer.documentsNeeded),
    },
    {
      key: "open",
      header: <span className="sr-only">Open</span>,
      align: "right",
      render: (customer) => (
        <Button
          variant="outline"
          size="sm"
          aria-label={`Details of ${customer.fullName}`}
          onClick={(event) => {
            event.stopPropagation();
            open(customer);
          }}
        >
          Details
        </Button>
      ),
    },
  ];
}

function CustomerCard({ customer, onOpen }: { customer: CustomerRow; onOpen: () => void }) {
  const bookingsLabel = `${customer.bookingsCount} booking${customer.bookingsCount === 1 ? "" : "s"}`;
  const line = [formatPhone(customer.phone), customer.cnic?.trim() || "Not added"].filter(Boolean).join(" · ");
  return (
    <button
      type="button"
      onClick={onOpen}
      className="block w-full cursor-pointer rounded-card border border-line bg-card p-4 text-left font-ui focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
    >
      <span className="flex flex-wrap items-center gap-2">
        <span className="text-section font-extrabold text-ink">{customer.fullName}</span>
        {customer.isBlocked && <StatusBadge status="Blocked" />}
      </span>
      <span className="mt-0.5 block text-small text-ink-muted">{line}</span>
      <span className="mt-3 flex flex-wrap items-center gap-2">
        <span className="text-small font-bold text-ink-2">{bookingsLabel}</span>
        {documentsBadge(customer.documentsNeeded)}
      </span>
    </button>
  );
}

function NewCustomerModal({
  open,
  onClose,
  onSaved,
}: {
  open: boolean;
  onClose: () => void;
  onSaved: (id: number) => void;
}) {
  const navigate = useNavigate();
  const [values, setValues] = useState(emptyCustomerForm);
  const [errors, setErrors] = useState<CustomerFormErrors>({});
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setValues(emptyCustomerForm());
    setErrors({});
    setFormError(null);
    setSaving(false);
  }, [open]);

  const save = async () => {
    const local = customerFormErrors(values);
    if (Object.keys(local).length) {
      setErrors(local);
      return;
    }
    setSaving(true);
    setFormError(null);
    setErrors({});
    try {
      const response = await api("/api/Customer", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payloadFromForm(values)),
      });
      const body = (await response.json().catch(() => ({}))) as ConflictBody & { id?: number };
      if (response.status === 409 && body.field && body.existingCustomerId && body.existingCustomerName) {
        const field = body.field === "phone" ? "mobile" : body.field === "cnic" ? "cnic" : null;
        if (field) {
          setErrors({
            [field]: duplicateFieldError(body.existingCustomerName, () => {
              onClose();
              navigate(`/customers/${body.existingCustomerId}`);
            }),
          });
        } else {
          setFormError(body.message ?? "This customer already exists.");
        }
        return;
      }
      if (!response.ok) {
        setFormError(body.message ?? "The customer could not be saved.");
        return;
      }
      if (!body.id) {
        setFormError("The customer was saved but no id was returned.");
        return;
      }
      onSaved(body.id);
    } catch {
      setFormError("The customer could not be saved.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="New customer"
      size="lg"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: "Save customer", onClick: () => void save(), loading: saving }}
    >
      {formError && (
        <p role="alert" className="mb-4 text-small font-bold text-danger">
          {formError}
        </p>
      )}
      <CustomerForm values={values} errors={errors} onChange={setValues} showNotes disabled={saving} />
    </Modal>
  );
}

function payloadFromForm(values: CustomerFormValues) {
  const text = (value: string) => {
    const trimmed = value.trim();
    return trimmed === "" ? null : trimmed;
  };
  return {
    fullName: values.fullName.trim(),
    fatherName: text(values.guardianName),
    phone: values.mobile.trim(),
    cnic: text(values.cnic),
    email: text(values.email),
    whatsapp: text(values.whatsapp),
    dateOfBirth: text(values.dob),
    nationality: text(values.nationality),
    occupation: text(values.occupation),
    address: text(values.address),
    notes: text(values.notes),
    source: "WalkIn",
  };
}
