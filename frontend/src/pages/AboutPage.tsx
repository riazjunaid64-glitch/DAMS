import { CompanyNumbers } from "../components/website/CompanyNumbers.tsx";
import { CallToActionBand } from "../components/website/CallToActionBand.tsx";
import { FadeUp } from "../components/website/FadeUp.tsx";
import { hoverLiftClass } from "../components/website/motion.ts";
import { cx } from "../components/ui/cx.ts";

const PAGE = "mx-auto flex w-full max-w-[1500px] flex-col gap-10 px-4 py-5 md:px-8 md:py-7";

const PRINCIPLES = [
  { number: "01", title: "Trust & transparency", text: "Honest advice and clear communication at every stage." },
  { number: "02", title: "Customer first", text: "We protect your interests, from site selection to handover." },
  { number: "03", title: "Quality delivery", text: "Every detail is managed to a high professional standard." },
] as const;

export default function AboutPage() {
  return (
    <div className={PAGE}>
      <section className="grid items-stretch gap-8 md:grid-cols-2 md:gap-10">
        <div className="min-w-0">
          <FadeUp>
            <p className="m-0 text-label font-bold tracking-widest text-gold-text">ABOUT DEEN ASSOCIATE</p>
          </FadeUp>
          <FadeUp order={1}>
            <h1 className="m-0 mt-3 max-w-xl text-3xl leading-tight font-extrabold text-ink md:text-4xl">
              Fulfilling promises with trust and confidence
            </h1>
          </FadeUp>
          <FadeUp order={2} className="mt-4 grid max-w-xl gap-4">
            <p className="m-0 text-body text-ink-2">
              Deen Associate helps buyers, sellers and investors across Islamabad with honest advice and long-term relationships, not one-time deals.
            </p>
            <p className="m-0 text-body text-ink-2">
              We guide you through every step: choosing the project, understanding its value, booking, paperwork and handover. Our projects include CPEC Greens, Deen Square, Mall of Faisal Hills, Urban Complex, Legacy Court and Seventeen Square.
            </p>
          </FadeUp>
        </div>
        <FadeUp order={3} className="flex flex-col">
          <div className="flex flex-1 flex-col overflow-hidden rounded-popup bg-primary">
            <CompanyNumbers layout="panel" stretch className="min-h-full flex-1" />
          </div>
        </FadeUp>
      </section>

      <section aria-labelledby="what-drives-us" className="grid gap-4">
        <FadeUp order={4}>
          <h2 id="what-drives-us" className="m-0 text-section font-extrabold text-ink">What drives us</h2>
        </FadeUp>
        <div className="grid gap-4 md:grid-cols-3">
          {PRINCIPLES.map((item, index) => (
            <FadeUp key={item.number} order={5 + index} className="h-full">
              <article className={cx("h-full rounded-card border border-line bg-card p-4 md:p-5", hoverLiftClass)}>
                <p className="m-0 text-small font-bold text-gold-text">{item.number}</p>
                <h3 className="m-0 mt-2 text-body font-extrabold text-ink">{item.title}</h3>
                <p className="m-0 mt-2 text-small leading-relaxed text-ink-muted">{item.text}</p>
              </article>
            </FadeUp>
          ))}
        </div>
      </section>

      <FadeUp order={8}>
        <CallToActionBand
          title="Looking for the right property?"
          message="Talk to our team about bookings, site visits and investment advice."
        />
      </FadeUp>
    </div>
  );
}
