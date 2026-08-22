using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace DAMS.Application.Services
{
    internal static class SimpleXlsxWriter
    {
        public static byte[] Write(string sheetName, IReadOnlyList<IReadOnlyList<object?>> rows)
        {
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                Add(archive, "[Content_Types].xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                      <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                      <Default Extension="xml" ContentType="application/xml"/>
                      <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                      <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                    </Types>
                    """);
                Add(archive, "_rels/.rels", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                    </Relationships>
                    """);
                Add(archive, "xl/workbook.xml", $"""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                      <sheets><sheet name="{Xml(sheetName)}" sheetId="1" r:id="rId1"/></sheets>
                    </workbook>
                    """);
                Add(archive, "xl/_rels/workbook.xml.rels", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                    </Relationships>
                    """);
                Add(archive, "xl/worksheets/sheet1.xml", Worksheet(rows));
            }
            return output.ToArray();
        }

        private static string Worksheet(IReadOnlyList<IReadOnlyList<object?>> rows)
        {
            var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                xml.Append("<row r=\"").Append(rowIndex + 1).Append("\">");
                var row = rows[rowIndex];
                for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
                {
                    var value = row[columnIndex];
                    if (value == null) continue;
                    var cell = ColumnName(columnIndex + 1) + (rowIndex + 1).ToString(CultureInfo.InvariantCulture);
                    if (value is byte or short or int or long or float or double or decimal)
                    {
                        xml.Append("<c r=\"").Append(cell).Append("\"><v>")
                            .Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append("</v></c>");
                    }
                    else
                    {
                        xml.Append("<c r=\"").Append(cell).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                            .Append(Xml(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)).Append("</t></is></c>");
                    }
                }
                xml.Append("</row>");
            }
            return xml.Append("</sheetData></worksheet>").ToString();
        }

        private static string ColumnName(int column)
        {
            var result = string.Empty;
            while (column > 0)
            {
                column--;
                result = (char)('A' + column % 26) + result;
                column /= 26;
            }
            return result;
        }

        private static string Xml(string value)
        {
            var cleaned = new string(value.Where(c => c is '\t' or '\n' or '\r' || c >= ' ').ToArray());
            return SecurityElement.Escape(cleaned) ?? string.Empty;
        }

        private static void Add(ZipArchive archive, string path, string value)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(value);
        }
    }
}
