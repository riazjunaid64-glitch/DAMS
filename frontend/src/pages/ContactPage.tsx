import Container from "../lib/Container";
import Section from "../lib/Section";

export default function ContactPage() {
  return (
    <main>
      <Section
        eyebrow="Contact"
        title="Start the Conversation"
        description="Send a message and our team will respond soon."
        className="bg-slate-950 py-16 sm:py-20"
      >
        <Container>
          <div className="grid gap-10 lg:grid-cols-2 lg:items-start">
            <div className="space-y-6 rounded-3xl border border-white/10 bg-white/5 p-8">
              <div>
                <h3 className="text-lg font-semibold">Visit Our Office</h3>
                <p className="mt-2 text-sm text-slate-300">123 Business Avenue, City Center</p>
              </div>
              <div>
                <h3 className="text-lg font-semibold">Support Hours</h3>
                <p className="mt-2 text-sm text-slate-300">
                  Sunday - Thursday: 9am - 6pm
                  <br />
                  Friday: Closed
                </p>
              </div>
            </div>
            <div className="rounded-3xl border border-white/10 bg-white/5 p-8">
              <h3 className="text-lg font-semibold">Contact Us</h3>
              <p className="mt-3 text-sm text-slate-300">
                This area can later include your real contact form or any CTA content.
              </p>
              <div className="mt-6 grid gap-3 text-sm text-slate-300">
                <p>Phone: +1 (555) 123-4567</p>
                <p>Email: contact@deenassociate.com</p>
              </div>
            </div>
          </div>
        </Container>
      </Section>
    </main>
  );
}
