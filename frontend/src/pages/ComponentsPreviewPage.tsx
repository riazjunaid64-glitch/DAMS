import { useState, type ReactNode } from "react";
import {
  ActionsMenu,
  AttachProof,
  Avatar,
  BottomSheet,
  Button,
  Card,
  Checkbox,
  ChoiceChips,
  ConfirmDialog,
  DataTable,
  DatePicker,
  Dropdown,
  EmptyState,
  FilterBar,
  IconAlert,
  IconArrowDown,
  IconArrowRight,
  IconArrowUp,
  IconCircle,
  IconDownload,
  IconFilter,
  IconInbox,
  IconPaperclip,
  IconPlus,
  InfoCard,
  KeyValueGrid,
  ListCard,
  LoadMore,
  Modal,
  Notice,
  NumberField,
  PageHeader,
  Pagination,
  PhotoGallery,
  PhotoSlider,
  RadioGroup,
  SearchBar,
  StatCard,
  StatSummary,
  StatusBadge,
  Tabs,
  TextArea,
  TextField,
  TimePicker,
  Toggle,
  useToast,
  type FilterValues,
  type PeriodPreset,
  type Photo,
} from "../components/ui";
import { buildPeriodRange } from "../lib/financePeriods.ts";
import { usePagedList } from "../lib/usePagedList.ts";

/*
 * Internal preview of the shared component library with dummy data, like the design package's
 * components.html. Dev builds, or admins in production (see the route in App.tsx).
 */

const OPTIONS = [1, 2, 3, 4].map((n) => ({ value: `o${n}`, label: `Option ${n}` }));
const COLOURS: [string, string][] = [
  ["Primary", "bg-primary"], ["Gold", "bg-gold"], ["Gold text", "bg-gold-text"], ["Gold soft", "bg-gold-soft"],
  ["Page", "bg-page"], ["Card", "bg-card"], ["Border", "bg-line"], ["Text", "bg-ink"], ["Text muted", "bg-ink-muted"],
  ["Success", "bg-success"], ["Danger", "bg-danger"], ["Warning", "bg-warning"],
];
const PHOTOS: Photo[] = ["floria-heights", "deen-square", "legacy-court", "urban-complex", "cpec-greens"].map((name, i) => ({
  id: name,
  src: `/images/projects/${name}-sm.webp`,
  alt: `Sample photo ${i + 1}`,
  isCover: i === 0,
}));
type Row = { id: number; name: string; sub: string; a: string; b: string; status: string };
const ROWS: Row[] = [1, 2, 3].map((n) => ({ id: n, name: `Row name ${n}`, sub: "Sub text", a: "Value", b: "Value", status: "In progress" }));
const MENU_ITEMS = [
  { label: "Menu action 1", icon: <IconCircle size={14} />, onSelect: () => {} },
  { label: "Menu action 2", icon: <IconCircle size={14} />, onSelect: () => {} },
  { label: "Menu action 3", icon: <IconCircle size={14} />, onSelect: () => {} },
  { label: "Danger action", icon: <IconCircle size={14} />, danger: true, onSelect: () => {} },
];

export default function ComponentsPreviewPage() {
  const inFrame = typeof window !== "undefined" && window.self !== window.top;
  return (
    <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-5 px-4 py-6 md:px-8">
      <PageHeader title="Shared components" subtitle="Build once, reuse on every screen. All text is dummy." />
      <ThemeSection />
      <ButtonSection />
      <DropdownSection />
      <SearchSection />
      <PaginationSection />
      <FieldsSection />
      <BadgeSection />
      <StatSection />
      <CardSection />
      <TabsSection />
      <MenuSection />
      <PopupSection />
      <EmptyToastPhotosSection />
      <LayoutSection />
      {!inFrame && (
        <Section n="15" title="Phone versions" note="this page at 390px wide">
          <iframe title="Phone preview" src="/dev/components" className="h-[780px] w-[390px] max-w-full rounded-card border border-line bg-page" />
        </Section>
      )}
    </div>
  );
}

function Section({ n, title, note, children }: { n: string; title: string; note?: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-4 rounded-card border border-line bg-card p-5">
      <div className="flex flex-wrap items-baseline gap-2.5">
        <span className="text-label font-extrabold text-gold-text">{n}</span>
        <h2 className="m-0 text-[17px] font-extrabold text-ink">{title}</h2>
        {note && <span className="text-small text-ink-muted">{note}</span>}
      </div>
      {children}
    </section>
  );
}

function Sample({ label, children, className }: { label: string; children: ReactNode; className?: string }) {
  return (
    <div className={`flex min-w-0 flex-col gap-2 ${className ?? ""}`}>
      <span className="text-caption font-bold uppercase tracking-[0.4px] text-ink-faint">{label}</span>
      {children}
    </div>
  );
}

const row = "flex flex-wrap items-start gap-5";

function ThemeSection() {
  return (
    <Section n="01" title="Theme basics" note="colours, text sizes, spacing">
      <div className="flex flex-wrap gap-2.5">
        {COLOURS.map(([name, bg]) => (
          <div key={name} className="flex w-[104px] flex-col gap-1.5">
            <div className={`h-[52px] rounded-field border border-line ${bg}`} />
            <span className="text-label font-bold">{name}</span>
          </div>
        ))}
      </div>
      <div className="flex flex-wrap items-end gap-8">
        <Sample label="Page title 28"><span className="text-page-title font-extrabold">Page title</span></Sample>
        <Sample label="Heading 18"><span className="text-section font-extrabold">Section heading</span></Sample>
        <Sample label="Body 15"><span className="text-body font-medium">Body text goes here</span></Sample>
        <Sample label="Label 12"><span className="text-label font-bold uppercase tracking-[0.4px]">Field label</span></Sample>
        <Sample label="Small 13"><span className="text-small">Helper text</span></Sample>
      </div>
    </Section>
  );
}

function ButtonSection() {
  return (
    <Section n="02" title="Button" note="one component, many types">
      <div className={row}>
        <Sample label="Primary"><Button>Button</Button></Sample>
        <Sample label="Outline"><Button variant="outline">Button</Button></Sample>
        <Sample label="Danger"><Button variant="danger">Button</Button></Sample>
        <Sample label="Success"><Button variant="success">Button</Button></Sample>
        <Sample label="Link"><Button variant="link">Button</Button></Sample>
        <Sample label="Disabled"><Button disabled>Button</Button></Sample>
        <Sample label="Loading"><Button loading>Saving</Button></Sample>
        <Sample label="With icon"><Button icon={<IconPlus size={16} />}>Button</Button></Sample>
        <Sample label="Icon only">
          <div className="flex gap-2">
            <Button iconOnly icon={<IconPlus size={18} />} aria-label="Add" />
            <Button iconOnly variant="outline" icon={<IconFilter size={18} />} aria-label="Filters" />
          </div>
        </Sample>
        <Sample label="Download, up, down, paperclip">
          <div className="flex items-center gap-3 text-ink">
            <IconDownload size={24} />
            <IconArrowUp size={24} />
            <IconArrowDown size={24} />
            <IconPaperclip size={24} />
          </div>
        </Sample>
      </div>
      <div className={row}>
        <Sample label="Small 34"><Button size="sm">Button</Button></Sample>
        <Sample label="Medium 40"><Button size="md">Button</Button></Sample>
        <Sample label="Large 48 (phone)"><Button size="lg">Button</Button></Sample>
        <Sample label="Full width (phone)" className="w-80"><Button size="lg" fullWidth>Button</Button></Sample>
      </div>
      <div className="flex flex-wrap items-center gap-3 rounded-card bg-primary p-4">
        <Button variant="gold" to="/projects">Explore projects <IconArrowRight size={16} /></Button>
        <Button variant="light" to="/contact">Contact us</Button>
      </div>
    </Section>
  );
}

function DropdownSection() {
  const [a, setA] = useState("");
  const [b, setB] = useState("o2");
  const [c, setC] = useState("o2");
  const [d, setD] = useState("");
  const [e, setE] = useState("o1");
  return (
    <Section n="03" title="Dropdown" note="form, filter and icon versions — closed and open">
      <div className="grid gap-5 pb-48 sm:grid-cols-2 lg:grid-cols-4">
        <Sample label="Form dropdown · empty"><Dropdown label="Label" required options={OPTIONS} value={a} onChange={setA} /></Sample>
        <Sample label="Form dropdown · filled"><Dropdown label="Label" options={OPTIONS} value={b} onChange={setB} /></Sample>
        <Sample label="Form dropdown · open"><Dropdown label="Label" options={OPTIONS} value={c} onChange={setC} defaultOpen /></Sample>
        <Sample label="Filter dropdown (compact) · open">
          <Dropdown size="filter" label="Filter" options={[{ value: "", label: "All" }, ...OPTIONS.slice(0, 3)]} value={d} onChange={setD} defaultOpen className="self-start" />
        </Sample>
        <Sample label="Dropdown with icon"><Dropdown aria-label="Role" icon={<IconFilter size={16} />} options={OPTIONS} value={e} onChange={setE} /></Sample>
        <Sample label="Disabled"><Dropdown label="Label" options={OPTIONS} value="o1" onChange={() => {}} disabled /></Sample>
        <Sample label="Error"><Dropdown label="Label" required options={OPTIONS} value="" onChange={() => {}} error="Choose an option" /></Sample>
      </div>
    </Section>
  );
}

function SearchSection() {
  const [search, setSearch] = useState("");
  const [typed, setTyped] = useState("Dummy text");
  const [values, setValues] = useState<FilterValues>({ status: "", type: "", from: "2026-01-01" });
  return (
    <Section n="04" title="Search bar" note="with filter bar and phone filter button">
      <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
        <Sample label="Empty"><SearchBar value={search} onSearch={setSearch} /></Sample>
        <Sample label="Typed (with clear)"><SearchBar value={typed} onSearch={setTyped} /></Sample>
        <Sample label="Phone size 44"><SearchBar size="lg" value="" onSearch={() => {}} /></Sample>
      </div>
      <Sample label="Filter bar (desktop) — on phone: search + filter button + add">
        <FilterBar
          search={{ value: search, onSearch: setSearch }}
          filters={[
            { type: "select", key: "status", label: "Filter", options: OPTIONS.slice(0, 3) },
            { type: "select", key: "type", label: "Filter", options: OPTIONS.slice(0, 3) },
            { type: "date", key: "from", label: "From" },
          ]}
          values={values}
          onChange={(changes) => setValues((current) => ({ ...current, ...changes }))}
          onReset={() => { setSearch(""); setValues({ status: "", type: "", from: "" }); }}
          onAdd={() => {}}
        />
      </Sample>
      <Sample label="Date range and Period — five filters on one line; Reset only when one is set">
        <PeriodFilterSample />
      </Sample>
      <Sample label="Financial year still loading">
        <LoadingYearSample />
      </Sample>
    </Section>
  );
}

const PERIOD_NOW = new Date(2026, 6, 15);
const periodRangeFor = (preset: Exclude<PeriodPreset, "custom">) => buildPeriodRange(preset, 7, PERIOD_NOW);

function PeriodFilterSample() {
  const [search, setSearch] = useState("");
  const [values, setValues] = useState<FilterValues>({ from: "", to: "", status: "", type: "" });
  return (
    <FilterBar
      search={{ value: search, onSearch: setSearch, placeholder: "Search" }}
      filters={[
        { type: "period", key: "period", fromKey: "from", toKey: "to", rangeFor: periodRangeFor, financialYear: "ready" },
        { type: "dateRange", fromKey: "from", toKey: "to", max: "2026-09-29" },
        { type: "select", key: "status", label: "Status", options: OPTIONS.slice(0, 3) },
        { type: "select", key: "type", label: "Type", options: OPTIONS.slice(0, 3) },
      ]}
      values={values}
      onChange={(changes) => setValues((current) => ({ ...current, ...changes }))}
      onReset={() => { setSearch(""); setValues({ from: "", to: "", status: "", type: "" }); }}
    />
  );
}

function LoadingYearSample() {
  const [values, setValues] = useState<FilterValues>({ from: "", to: "" });
  return (
    <FilterBar
      filters={[{ type: "period", key: "period", fromKey: "from", toKey: "to", rangeFor: periodRangeFor, financialYear: "loading" }]}
      values={values}
      onChange={(changes) => setValues((current) => ({ ...current, ...changes }))}
      onReset={() => setValues({ from: "", to: "" })}
    />
  );
}

function PaginationSection() {
  const [page, setPage] = useState(1);
  const [middle, setMiddle] = useState(5);
  const [shown, setShown] = useState(20);
  return (
    <Section n="05" title="Pagination" note="20 per page; phone uses Load more">
      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_320px]">
        <div className="flex flex-col gap-5">
          <Sample label="Desktop · first page"><Pagination page={page} onPageChange={setPage} totalCount={200} /></Sample>
          <Sample label="Desktop · middle page"><Pagination page={middle} onPageChange={setMiddle} totalCount={200} /></Sample>
        </div>
        <Sample label="Phone · load more"><LoadMore shown={shown} total={200} onLoadMore={() => setShown((n) => Math.min(n + 20, 200))} /></Sample>
        <Sample label="Paged list hook — numbers on desktop, Load more on a phone"><PagedListSample /></Sample>
      </div>
    </Section>
  );
}

function PagedListSample() {
  const list = usePagedList({
    queryKey: "preview",
    fetchPage: async ({ skip, take }) => {
      const totalCount = 64;
      const items = Array.from({ length: Math.min(take, Math.max(0, totalCount - skip)) }, (_, index) => ({ id: skip + index + 1 }));
      return { items, totalCount, hasMore: skip + items.length < totalCount };
    },
  });
  return (
    <div className="flex flex-col gap-3">
      <p className="m-0 text-small text-ink-muted">{list.loading && list.rows.length === 0 ? "Loading…" : `Rows ${list.rows.length ? `${list.rows[0]!.id}–${list.rows[list.rows.length - 1]!.id}` : "none"}`}</p>
      <div className="hidden md:block"><Pagination {...list.pagination} itemLabel="entries" /></div>
      <div className="md:hidden"><LoadMore {...list.loadMoreBar} /></div>
    </div>
  );
}

function DatePickerStates() {
  const [payment, setPayment] = useState("2026-09-29");
  const [birth, setBirth] = useState("1988-03-14");
  const [optional, setOptional] = useState("");
  return (
    <>
      <DatePicker label="Payment date" required value={payment} onChange={setPayment} max="2026-09-29" helper="Past dates only" />
      <DatePicker label="Date of birth" value={birth} onChange={setBirth} />
      <DatePicker label="Follow-up date" value={optional} onChange={setOptional} />
      <DatePicker label="Refund date" value="2026-09-29" onChange={() => {}} locked />
    </>
  );
}

function TimePickerStates() {
  const [time, setTime] = useState("11:30");
  const [empty, setEmpty] = useState("");
  return (
    <>
      <TimePicker label="Time" required value={time} onChange={setTime} />
      <TimePicker label="Time" value={empty} onChange={setEmpty} />
    </>
  );
}

function AttachProofStates() {
  const [file, setFile] = useState<File | null>(null);
  return (
    <>
      <AttachProof file={file && { name: file.name, size: file.size, uploaded: false }} onPick={setFile} onRemove={() => setFile(null)} />
      <AttachProof file={null} error="That file is over 15 MB. Choose a smaller one." onPick={() => {}} />
      <AttachProof file={{ name: "transfer-slip.jpg", size: 1000 }} progress={60} onPick={() => {}} onRemove={() => {}} />
      <AttachProof file={{ name: "transfer-slip-from-the-bank-on-the-first-of-the-month.jpg", size: 240 * 1024, uploaded: true, onOpen: () => {} }} onPick={() => {}} onRemove={() => {}} />
      <SavedProofSample />
    </>
  );
}

function SavedProofSample() {
  return (
    <AttachProof
      label="Saved proof"
      file={{
        name: "development-charges-receipt.pdf",
        size: 220 * 1024,
        uploaded: true,
        onOpen: () => {},
        onDownload: () => {},
      }}
      onPick={() => {}}
      onReplace={() => {}}
      onPendingRemove={() => {}}
    />
  );
}

function NegativeAmountSample() {
  const [value, setValue] = useState("-1200000");
  return <NumberField label="Opening balance" prefix="Rs" allowNegative value={value} onChange={setValue} decimals={0} />;
}

function FieldsSection() {
  const [amount, setAmount] = useState("1000000");
  const [size, setSize] = useState("100");
  const [chip, setChip] = useState("o2");
  const [radio, setRadio] = useState("o1");
  const [check, setCheck] = useState(true);
  const [check2, setCheck2] = useState(false);
  const [on, setOn] = useState(true);
  const [off, setOff] = useState(false);
  return (
    <Section n="06" title="Form fields" note="all input types and states">
      <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
        <TextField label="Label" required placeholder="Placeholder" helper="Helper text" />
        <TextField label="Label" defaultValue="Dummy value" />
        <TextField label="Label" required placeholder="Placeholder" error="This field is required" />
        <TextField label="Label" defaultValue="Dummy value" disabled />
        <NumberField label="Amount" prefix="Rs" value={amount} onChange={setAmount} />
        <NumberField label="Size" suffix="sq ft" value={size} onChange={setSize} decimals={0} />
        <NegativeAmountSample />
        <DatePickerStates />
        <TimePickerStates />
        <TextArea label="Notes" placeholder="Write something…" className="sm:col-span-2" />
      </div>
      <div className="mt-5 grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
        <AttachProofStates />
      </div>
      <div className={row}>
        <ChoiceChips label="Label" options={OPTIONS} value={chip} onChange={setChip} />
        <ChoiceChips label="Segmented" variant="segmented" options={OPTIONS.slice(0, 2)} value={chip} onChange={setChip} />
        <RadioGroup label="Radio" options={OPTIONS.slice(0, 2)} value={radio} onChange={setRadio} />
        <Sample label="Checkbox">
          <Checkbox label="Option 1" checked={check} onChange={setCheck} />
          <Checkbox label="Option 2" checked={check2} onChange={setCheck2} />
        </Sample>
        <Sample label="Toggle">
          <Toggle label="On" checked={on} onChange={setOn} />
          <Toggle label="Off" checked={off} onChange={setOff} />
        </Sample>
      </div>
    </Section>
  );
}

function BadgeSection() {
  return (
    <Section n="07" title="Status badge" note="fixed colour set — screens pass only the status">
      <div className="flex flex-wrap gap-3">
        {["In progress", "Ongoing", "Won", "Available", "Completed", "Lost", "Dormant", "Booked", "Planning", "Needs details", "Sold", "Highlight"].map((status) => (
          <StatusBadge key={status} status={status} />
        ))}
      </div>
    </Section>
  );
}

function StatSection() {
  const [selected, setSelected] = useState(true);
  return (
    <Section n="08" title="Stat / info card">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Sample label="Stat card"><StatCard label="Label" value={100} /></Sample>
        <Sample label="Stat card · selected (acts as filter)"><StatCard label="Label" value={100} selected={selected} onClick={() => setSelected((v) => !v)} /></Sample>
        <Sample label="Coloured label"><StatCard label="Label" value={60} tone="green" /></Sample>
        <Sample label="Note"><StatCard label="Balance" value="Rs 12,450,000" note="As of today" /></Sample>
        <Sample label="Highlight"><StatCard label="Needs attention" value="Rs 80,000" highlight note="As of today" /></Sample>
        <Sample label="Loading"><StatCard label="Balance" value="Rs 0" state="loading" /></Sample>
        <Sample label="Error"><StatCard label="Balance" value="Rs 0" state="error" note="As of today" /></Sample>
        <Sample label="Info card (highlight)"><InfoCard highlight label="Label" value="Main value" detail="Small detail" /></Sample>
        <Sample label="Info card"><InfoCard label="Label" value="Main value" detail="Small detail" /></Sample>
      </div>
      <Sample label="Stat summary — phone shows the navy Total bar and a 2×2 grid">
        <StatSummary
          total={{ label: "Total", value: 100 }}
          items={[
            { label: "In progress", value: 40, tone: "blue" },
            { label: "Won", value: 30, tone: "green" },
            { label: "Lost", value: 20, tone: "red" },
            { label: "Dormant", value: 10, tone: "orange" },
          ]}
        />
      </Sample>
    </Section>
  );
}

function CardSection() {
  return (
    <Section n="09" title="Card, list card, table">
      <div className="grid gap-5 lg:grid-cols-2">
        <Sample label="Section card with key / value">
          <Card title="Section title" action={{ label: "Action", onClick: () => {} }}>
            <KeyValueGrid items={[
              { label: "Label", value: "Dummy value" }, { label: "Label", value: "Dummy value" },
              { label: "Label", value: "Dummy value" }, { label: "Label", value: null },
            ]} />
          </Card>
        </Sample>
        <Sample label="List card (tap to open, no button)">
          <ListCard reference="Item ID" status="Available" title="Item title" detail="Detail · Detail" value="Main value" onClick={() => {}} />
          <ListCard reference="Item ID" status="Available" title="Selected item" detail="Navy border" value="Main value" selected onClick={() => {}} />
        </Sample>
      </div>
      <Sample label="Table (desktop lists)">
        <DataTable
          rows={ROWS}
          rowKey={(r) => r.id}
          onRowClick={() => {}}
          rowLabel={(r) => `Open ${r.name}`}
          columns={[
            { key: "name", header: "Name", render: (r) => <><p className="m-0 font-extrabold">{r.name}</p><p className="m-0 text-label text-ink-muted">{r.sub}</p></> },
            { key: "a", header: "Column", render: (r) => r.a },
            { key: "b", header: "Column", render: (r) => r.b },
            { key: "status", header: "Status", render: (r) => <StatusBadge status={r.status}>Status</StatusBadge> },
            { key: "open", header: "", align: "right", render: () => <Button variant="outline" size="sm">Open</Button> },
          ]}
          phoneCard={(r) => <ListCard reference={`#${r.id}`} status={r.status} title={r.name} detail={r.sub} value={r.a} onClick={() => {}} />}
        />
      </Sample>
      <Sample label="Group, subtotal and total">
        <GroupedTable />
      </Sample>
      <Sample label="Loading — placeholders, then the rows stay on a refresh">
        <LoadingTable />
      </Sample>
    </Section>
  );
}

type GroupRow = { id: string; kind?: "group" | "subtotal" | "total"; name: string; amount: string };

const GROUP_ROWS: GroupRow[] = [
  { id: "g", kind: "group", name: "Cash and bank", amount: "" },
  { id: "1", name: "Petty cash", amount: "Rs 12,000" },
  { id: "s", kind: "subtotal", name: "Subtotal", amount: "Rs 12,000" },
  { id: "t", kind: "total", name: "Total", amount: "Rs 12,000" },
];

function GroupedTable() {
  return (
    <DataTable
      rows={GROUP_ROWS}
      rowKey={(row) => row.id}
      columns={[
        { key: "name", header: "Name", render: (row) => row.name },
        { key: "amount", header: "Amount", align: "right", render: (row) => row.amount },
      ]}
      phoneCard={(row) => <ListCard title={row.name} value={row.amount} />}
    />
  );
}

function LoadingTable() {
  const [mode, setMode] = useState<"loading" | "ready" | "refresh">("loading");
  return (
    <div className="flex flex-col gap-3">
      <Button
        variant="outline"
        size="sm"
        onClick={() => setMode((current) => current === "loading" ? "ready" : current === "ready" ? "refresh" : "ready")}
      >
        {mode === "loading" ? "Show rows" : mode === "ready" ? "Refresh" : "Done"}
      </Button>
      <DataTable
        loading={mode !== "ready"}
        rows={mode === "loading" ? [] : GROUP_ROWS}
        rowKey={(row) => row.id}
        columns={[
          { key: "name", header: "Name", render: (row) => row.name },
          { key: "amount", header: "Amount", align: "right", render: (row) => row.amount },
        ]}
        phoneCard={(row) => <ListCard title={row.name} value={row.amount} />}
      />
    </div>
  );
}

function TabsSection() {
  const [three, setThree] = useState("one");
  const [five, setFive] = useState("overview");
  return (
    <Section n="10" title="Tabs" note="on phone, 4+ tabs become a Show dropdown">
      <div className={row}>
        <Sample label="Tabs (desktop, and phone when 3 or fewer)">
          <Tabs value={three} onChange={setThree} items={[{ id: "one", label: "Tab one" }, { id: "two", label: "Tab two", count: 3 }, { id: "three", label: "Tab three", count: 3 }]} />
        </Sample>
        <Sample label="Five tabs (dropdown on phone)">
          <Tabs value={five} onChange={setFive} items={["Overview", "Timeline", "Communications", "Follow-ups", "Site visits"].map((label) => ({ id: label.toLowerCase(), label }))} />
        </Sample>
      </div>
    </Section>
  );
}

function MenuSection() {
  return (
    <Section n="11" title="Actions menu">
      <div className={`${row} pb-52`}>
        <Sample label="Actions button · open"><ActionsMenu items={MENU_ITEMS} defaultOpen /></Sample>
        <Sample label="⋯ menu"><ActionsMenu trigger="dots" items={MENU_ITEMS} /></Sample>
      </div>
    </Section>
  );
}

function PopupSection() {
  const [open, setOpen] = useState<null | "form" | "full" | "confirm" | "sheet">(null);
  const [value, setValue] = useState("o1");
  const close = () => setOpen(null);
  return (
    <Section n="12" title="Popup" note="Esc and backdrop close; focus stays inside">
      <div className="flex flex-wrap gap-3">
        <Button variant="outline" onClick={() => setOpen("form")}>Form popup</Button>
        <Button variant="outline" onClick={() => setOpen("full")}>Long form (full screen on phone)</Button>
        <Button variant="outline" onClick={() => setOpen("confirm")}>Confirm popup</Button>
        <Button variant="outline" onClick={() => setOpen("sheet")}>Bottom sheet</Button>
      </div>
      <Modal open={open === "form"} onClose={close} title="Popup title" primaryAction={{ label: "Save", onClick: close }}>
        <div className="flex flex-col gap-4">
          <TextField label="Label" required placeholder="Placeholder" />
          <Dropdown label="Label" options={OPTIONS} value={value} onChange={setValue} />
        </div>
      </Modal>
      <Modal open={open === "full"} onClose={close} title="Edit lead" size="lg" phoneLayout="fullscreen" primaryAction={{ label: "Save", onClick: close }}>
        <div className="grid gap-4 sm:grid-cols-2">
          {Array.from({ length: 8 }, (_, i) => <TextField key={i} label={`Field ${i + 1}`} placeholder="Placeholder" />)}
          <Dropdown label="Label" options={OPTIONS} value={value} onChange={setValue} />
        </div>
      </Modal>
      <ConfirmDialog open={open === "confirm"} onClose={close} onConfirm={close} message="Short dummy message explaining what will happen." danger />
      <BottomSheet open={open === "sheet"} onClose={close} title="Filters" onReset={close} onApply={close}>
        <div className="flex flex-col gap-4">
          <Dropdown label="Status" options={OPTIONS} value={value} onChange={setValue} />
          <Dropdown label="Source" options={OPTIONS} value="" onChange={() => {}} />
        </div>
      </BottomSheet>
    </Section>
  );
}

function EmptyToastPhotosSection() {
  const toast = useToast();
  const [photos, setPhotos] = useState(PHOTOS.slice(0, 3));
  return (
    <Section n="13" title="Empty state, notice, toast, photos">
      <div className="grid gap-5 lg:grid-cols-3">
        <Sample label="Empty state">
          <EmptyState title="Nothing here yet" message="Short dummy helper text" action={<Button variant="outline">Action</Button>} />
        </Sample>
        <Sample label="Notice · gold with action (button drops below on phone)" className="lg:col-span-2">
          <Notice
            tone="gold"
            icon={<IconInbox size={18} />}
            title="3 items waiting"
            message="Short dummy helper text"
            action={<Button variant="outline" size="sm">Review</Button>}
          />
          <Notice tone="red" icon={<IconAlert size={18} />} title="Something needs fixing" message="Short dummy helper text" />
        </Sample>
        <Sample label="Toast · success / error">
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" onClick={() => toast.success("Saved successfully")}>Show success</Button>
            <Button variant="outline" onClick={() => toast.error("Something went wrong. Try again.")}>Show error</Button>
          </div>
        </Sample>
        <Sample label="Photo slider"><PhotoSlider photos={PHOTOS} /></Sample>
      </div>
      <Sample label="Photo gallery (cover tag, ⋯ menu, upload tile)">
        <PhotoGallery
          photos={photos}
          canManage
          onSetCover={(photo) => setPhotos((all) => all.map((p) => ({ ...p, isCover: p.id === photo.id })))}
          onDelete={(photo) => setPhotos((all) => all.filter((p) => p.id !== photo.id))}
          onUpload={(files) => toast.success(`${files.length} photo(s) chosen`)}
        />
      </Sample>
    </Section>
  );
}

function LayoutSection() {
  return (
    <Section n="14" title="App layout + phone pieces" note="the shell around this page is the real one">
      <Sample label="Page header">
        <PageHeader
          back={{ to: "/dev/components", label: "Back" }}
          title="Page title"
          status="In progress"
          subtitle="Sub line with one short detail"
          actions={<><Button variant="outline">Secondary</Button><ActionsMenu items={MENU_ITEMS} /></>}
        />
      </Sample>
      <Sample label="Avatar">
        <div className="flex gap-2"><Avatar name="Ahmed Khan" /><Avatar name="Sara" size={44} /></div>
      </Sample>
    </Section>
  );
}
