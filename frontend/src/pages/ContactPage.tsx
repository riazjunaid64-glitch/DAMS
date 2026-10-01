import {
  Button,
  IconArrowUpRight,
  IconChevronRight,
  IconClock,
  IconMail,
  IconMapPin,
  IconPhone,
  IconWhatsapp,
} from "../components/ui";
import { cx } from "../components/ui/cx.ts";
import { FadeUp } from "../components/website/FadeUp.tsx";
import { hoverLiftClass } from "../components/website/motion.ts";
import { SITE_CONTACT } from "../lib/siteContact.ts";

const PAGE = "mx-auto flex w-full max-w-[1500px] flex-col gap-8 px-4 py-5 md:px-8 md:py-7";

const CARDS = [
  {
    title: "Message on WhatsApp",
    value: SITE_CONTACT.phoneMobile,
    href: SITE_CONTACT.whatsappUrl,
    external: true,
    navy: true,
    icon: <IconWhatsapp />,
  },
  {
    title: "Call the office",
    value: SITE_CONTACT.phone,
    href: `tel:${SITE_CONTACT.phoneTel}`,
    external: false,
    navy: false,
    icon: <IconPhone />,
  },
  {
    title: "Send an email",
    value: SITE_CONTACT.email,
    href: `mailto:${SITE_CONTACT.email}`,
    external: false,
    navy: false,
    icon: <IconMail />,
  },
] as const;

export default function ContactPage() {
  return (
    <div className={PAGE}>
      <header>
        <FadeUp>
          <p className="m-0 text-label font-bold tracking-widest text-gold-text">CONTACT US</p>
        </FadeUp>
        <FadeUp order={1} className="mt-3 max-w-xl">
          <h1 className="m-0 text-3xl leading-tight font-extrabold text-ink md:text-4xl">Get in touch</h1>
          <p className="m-0 mt-3 text-body text-ink-2">
            Talk to our team about bookings, site visits and investment plans.
          </p>
        </FadeUp>
      </header>

      <div className="grid gap-4 md:grid-cols-3">
        {CARDS.map((card, index) => (
          <FadeUp key={card.title} order={2 + index} className="h-full">
            <a
              href={card.href}
              target={card.external ? "_blank" : undefined}
              rel={card.external ? "noopener noreferrer" : undefined}
              className={cx(
                "relative flex h-full items-center gap-3 rounded-card border p-4 no-underline md:block md:p-5",
                card.navy ? "border-primary bg-primary text-white" : "border-line bg-card text-ink",
                hoverLiftClass,
              )}
            >
              <span
                className={cx(
                  "inline-flex size-11 items-center justify-center rounded-full",
                  card.navy ? "bg-gold text-ink" : "bg-gold-soft text-gold-text",
                )}
              >
                {card.icon}
              </span>
              <span className="min-w-0 flex-1 md:mt-4">
                <span className={cx("block font-extrabold", card.navy ? "text-white" : "text-ink")}>{card.title}</span>
                <span className={cx("mt-0.5 block text-small", card.navy ? "text-white/80" : "text-ink-muted")}>
                  {card.value}
                </span>
              </span>
              <IconArrowUpRight className="absolute top-4 right-4 hidden md:block" />
              <IconChevronRight className="shrink-0 md:hidden" />
            </a>
          </FadeUp>
        ))}
      </div>

      <section className="grid items-stretch gap-4 md:grid-cols-2" aria-labelledby="visit-us">
        <FadeUp order={5} className="h-full">
          <article className="flex h-full flex-col rounded-card border border-line bg-card p-4 md:p-5">
            <h2 id="visit-us" className="m-0 text-section font-extrabold text-ink">Visit us</h2>
            <div className="mt-4 flex gap-3">
              <span className="mt-0.5 text-gold-text"><IconMapPin /></span>
              <p className="m-0 text-body text-ink">{SITE_CONTACT.address}</p>
            </div>
            <div className="mt-3 flex gap-3">
              <span className="mt-0.5 text-gold-text"><IconClock /></span>
              <p className="m-0 text-body text-ink">{SITE_CONTACT.hours}</p>
            </div>
            <div className={cx("mt-5", hoverLiftClass)}>
              <Button
                variant="outline"
                fullWidth
                href={SITE_CONTACT.mapsUrl}
                target="_blank"
                rel="noopener noreferrer"
                icon={<IconMapPin size={16} />}
              >
                Get directions
              </Button>
            </div>
          </article>
        </FadeUp>
        <FadeUp order={6} className="h-[200px] md:h-auto">
          <div className="h-full overflow-hidden rounded-card border border-line bg-card">
            <iframe
              title="Deen Associate office location"
              src={SITE_CONTACT.mapsEmbedUrl}
              loading="lazy"
              referrerPolicy="no-referrer-when-downgrade"
              className="block h-full w-full border-0"
            />
          </div>
        </FadeUp>
      </section>
    </div>
  );
}
