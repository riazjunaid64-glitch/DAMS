const OFFICE = {
  lat: 33.698889,
  lng: 73.048333,
  query: "Office 17, Allahdaad Plaza, G-8 Markaz, Islamabad",
} as const;

export const SITE_CONTACT = {
  brandName: "Deen Associate",
  logoNavSrc: "/images/logo-nav-white.png",
  description:
    "Deen Associate helps clients buy, sell, and manage property with transparency and trust. Track projects, units, bookings, and progress — all in one place.",
  email: "info@thedeenassociates.com",
  phone: "+92 (51) 2340061",
  phoneMobile: "+92 333 5165466",
  phoneTel: "+92512340061",
  phoneMobileTel: "+923335165466",
  whatsappUrl: "https://wa.me/923335165466",
  website: "https://www.thedeenassociates.com",
  address: "Office 17, 1st Floor, Allahdaad Plaza, G-8 Markaz, Islamabad, Pakistan.",
  mapsUrl: `https://www.google.com/maps/search/?api=1&query=${OFFICE.lat},${OFFICE.lng}`,
  mapsEmbedUrl: `https://maps.google.com/maps?q=${OFFICE.lat},${OFFICE.lng}&z=17&hl=en&output=embed`,
  hours: "Sun–Thu: 9am – 6pm",
} as const;

export const SITE_MAP = OFFICE;
