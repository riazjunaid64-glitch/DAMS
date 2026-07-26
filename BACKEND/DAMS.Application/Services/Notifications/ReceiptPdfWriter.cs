using System.Globalization;
using System.Text;
using DAMS.Application.DTOs.BookingDtos;

namespace DAMS.Application.Services.Notifications
{
    /// <summary>
    /// Produces the official receipt PDF that is attached to a payment receipt email.
    ///
    /// It is a minimal, dependency-free PDF writer: a single page laid out with the standard
    /// Helvetica fonts every reader has built in. Every number and name comes from the
    /// <see cref="PaymentReceiptDto"/> that DAMS already builds for the on-screen receipt, so
    /// the attachment and the screen can never disagree about what was paid.
    /// </summary>
    public static class ReceiptPdfWriter
    {
        private const float PageWidth = 595f;   // A4 at 72 dpi
        private const float PageHeight = 842f;
        private const float Margin = 56f;

        public static byte[] Build(PaymentReceiptDto receipt, string companyName, string? companyAddress,
            string? supportEmail, string? supportPhone, string currencySymbol)
        {
            var content = new StringBuilder();
            var y = PageHeight - Margin;

            void Text(string value, float size, bool bold, float indent = 0)
            {
                content.Append("BT /")
                       .Append(bold ? "F2" : "F1")
                       .Append(' ').Append(F(size)).Append(" Tf ")
                       .Append(F(Margin + indent)).Append(' ').Append(F(y)).Append(" Td (")
                       .Append(Escape(value))
                       .Append(") Tj ET\n");
            }

            void Line(float thickness = 0.7f)
            {
                content.Append(F(thickness)).Append(" w ")
                       .Append(F(Margin)).Append(' ').Append(F(y)).Append(" m ")
                       .Append(F(PageWidth - Margin)).Append(' ').Append(F(y)).Append(" l S\n");
            }

            void Row(string label, string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                content.Append("BT /F1 10 Tf ").Append(F(Margin)).Append(' ').Append(F(y)).Append(" Td (")
                       .Append(Escape(label)).Append(") Tj ET\n");
                content.Append("BT /F2 10 Tf ").Append(F(Margin + 170)).Append(' ').Append(F(y)).Append(" Td (")
                       .Append(Escape(value)).Append(") Tj ET\n");
                y -= 17f;
            }

            Text(companyName, 17, bold: true);
            y -= 20f;

            if (!string.IsNullOrWhiteSpace(companyAddress))
            {
                Text(companyAddress, 9, bold: false);
                y -= 13f;
            }

            var contact = string.Join("   |   ", new[] { supportEmail, supportPhone }.Where(v => !string.IsNullOrWhiteSpace(v)));
            if (contact.Length > 0)
            {
                Text(contact, 9, bold: false);
                y -= 13f;
            }

            y -= 8f;
            Line(1.2f);
            y -= 24f;

            Text("OFFICIAL PAYMENT RECEIPT", 13, bold: true);
            y -= 26f;

            Row("Receipt number", receipt.ReceiptNumber ?? "—");
            Row("Payment date", receipt.PaidAt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
            Row("Booking reference", receipt.BookingReference);
            Row("Received by", receipt.ReceivedByName);

            y -= 6f;
            Line(0.5f);
            y -= 20f;

            Text("Customer", 11, bold: true);
            y -= 18f;
            Row("Name", receipt.CustomerName);
            Row("S/o, D/o, W/o", receipt.FatherName);
            Row("Phone", receipt.CustomerPhone);
            Row("Address", Shorten(receipt.CustomerAddress, 60));

            y -= 6f;
            Line(0.5f);
            y -= 20f;

            Text("Property", 11, bold: true);
            y -= 18f;
            Row("Project", receipt.ProjectName);
            Row("Unit", receipt.UnitNumber);
            Row("Type", receipt.UnitType);
            Row("Block", receipt.Block);
            Row("Floor", receipt.FloorNumber > 0 ? receipt.FloorNumber.ToString(CultureInfo.InvariantCulture) : null);
            Row("Size", receipt.UnitSize > 0 ? $"{receipt.UnitSize.ToString("N2", CultureInfo.InvariantCulture)} sq ft" : null);

            y -= 6f;
            Line(0.5f);
            y -= 20f;

            Text("Payment", 11, bold: true);
            y -= 18f;
            Row("Payment type", receipt.Type.ToString());
            Row("Method", receipt.PaymentMethod.ToString());
            Row("Reference", receipt.PaymentReference);
            if (receipt.InstallmentSequence.HasValue)
                Row("Installment", $"#{receipt.InstallmentSequence}{(receipt.InstallmentType.HasValue ? $" ({receipt.InstallmentType})" : string.Empty)}");

            y -= 8f;
            Line(0.5f);
            y -= 26f;

            content.Append("BT /F2 14 Tf ").Append(F(Margin)).Append(' ').Append(F(y)).Append(" Td (")
                   .Append(Escape("Amount received")).Append(") Tj ET\n");
            content.Append("BT /F2 14 Tf ").Append(F(Margin + 250)).Append(' ').Append(F(y)).Append(" Td (")
                   .Append(Escape($"{currencySymbol} {receipt.Amount.ToString("N2", CultureInfo.InvariantCulture)}")).Append(") Tj ET\n");
            y -= 30f;

            Line(1.2f);
            y -= 22f;

            Text("This is a computer-generated receipt and is valid without a signature.", 8.5f, bold: false);
            y -= 12f;
            Text($"Generated {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC.", 8.5f, bold: false);

            return Assemble(content.ToString());
        }

        /// <summary>Writes the objects, the cross-reference table and the trailer.</summary>
        private static byte[] Assemble(string content)
        {
            var contentBytes = Encoding.ASCII.GetBytes(content);

            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(PageWidth)} {F(PageHeight)}] " +
                "/Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> /Contents 4 0 R >>",
                $"<< /Length {contentBytes.Length} >>\nstream\n{content}endstream",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"
            };

            var output = new MemoryStream();
            var writer = new StreamWriter(output, Encoding.ASCII) { NewLine = "\n" };
            writer.Write("%PDF-1.4\n");
            writer.Flush();

            var offsets = new List<long>();
            for (var i = 0; i < objects.Count; i++)
            {
                writer.Flush();
                offsets.Add(output.Position);
                writer.Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }

            writer.Flush();
            var xrefPosition = output.Position;

            writer.Write($"xref\n0 {objects.Count + 1}\n");
            writer.Write("0000000000 65535 f \n");
            foreach (var offset in offsets)
                writer.Write($"{offset:D10} 00000 n \n");

            writer.Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefPosition}\n%%EOF\n");
            writer.Flush();

            return output.ToArray();
        }

        private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>PDF strings are parenthesised; the delimiters and the escape must be escaped.</summary>
        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '(': builder.Append("\\("); break;
                    case ')': builder.Append("\\)"); break;
                    case '\r':
                    case '\n': builder.Append(' '); break;
                    default:
                        // The stream is written as ASCII, so anything outside it becomes a
                        // placeholder rather than silently corrupting the byte count.
                        builder.Append(c <= 0x7E && c >= 0x20 ? c : '?');
                        break;
                }
            }

            return builder.ToString();
        }

        private static string? Shorten(string? value, int max) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..(max - 3)] + "...";
    }
}
