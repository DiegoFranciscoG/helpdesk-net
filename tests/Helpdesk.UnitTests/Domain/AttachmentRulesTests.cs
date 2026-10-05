using System.Text;
using Helpdesk.Domain.Attachments;

namespace Helpdesk.UnitTests.Domain;

public sealed class AttachmentRulesTests
{
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n%âãÏÓ\n"u8.ToArray(), .. new byte[64]];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private static readonly byte[] WindowsExecutable = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];

    [Theory]
    [InlineData("estado-de-cuenta.pdf", "application/pdf")]
    [InlineData("ESTADO.PDF", "application/pdf")]
    public void Accepts_pdf(string name, string contentType) => Assert(name, Pdf, contentType);

    [Fact]
    public void Accepts_png() => Assert("captura.png", Png, "image/png");

    [Theory]
    [InlineData("foto.jpg")]
    [InlineData("foto.jpeg")]
    public void Accepts_jpeg(string name) => Assert(name, Jpeg, "image/jpeg");

    [Fact]
    public void Accepts_utf8_text_with_accents() =>
        Assert("detalle.txt", Encoding.UTF8.GetBytes("Transferencia rechazada a las 10:15, código 51. Atención: año 2026."), "text/plain; charset=utf-8");

    [Fact]
    public void Rejects_an_executable_renamed_as_pdf() =>
        Reject("factura.pdf", WindowsExecutable, "attachment_signature_mismatch");

    [Fact]
    public void Rejects_a_png_renamed_as_jpeg() =>
        Reject("foto.jpg", Png, "attachment_signature_mismatch");

    [Fact]
    public void Rejects_double_extensions_ending_in_an_executable() =>
        Reject("factura.pdf.exe", Pdf, "attachment_type_not_allowed");

    [Theory]
    [InlineData("macro.docx")]
    [InlineData("comprimido.zip")]
    [InlineData("script.svg")]
    [InlineData("pagina.html")]
    [InlineData("sin-extension")]
    public void Rejects_types_outside_the_allowlist(string name) =>
        Reject(name, Pdf, "attachment_type_not_allowed");

    [Fact]
    public void Rejects_text_with_null_bytes() =>
        Reject("datos.txt", [0x48, 0x00, 0x6F, 0x6C, 0x61], "attachment_signature_mismatch");

    [Fact]
    public void Rejects_text_with_invalid_utf8() =>
        Reject("datos.txt", [0x48, 0x6F, 0xC3, 0x28], "attachment_signature_mismatch");

    [Fact]
    public void Rejects_empty_files() => Reject("vacio.pdf", [], "attachment_empty");

    [Fact]
    public void Rejects_files_over_five_megabytes()
    {
        var big = new byte[AttachmentRules.MaxBytes + 1];
        Pdf.CopyTo(big, 0);

        Reject("grande.pdf", big, "attachment_too_large");
    }

    [Fact]
    public void Accepts_a_file_of_exactly_five_megabytes()
    {
        var limit = new byte[AttachmentRules.MaxBytes];
        Pdf.CopyTo(limit, 0);

        AttachmentRules.Inspect("limite.pdf", limit).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData(@"C:\Users\ana\Desktop\captura.png", "captura.png")]
    [InlineData("nombre<raro>:|?*.pdf", "nombre_raro_____.pdf")]
    [InlineData("  .oculto.pdf  ", "oculto.pdf")]
    [InlineData("tab\tnueva\nlinea.txt", "tab_nueva_linea.txt")]
    [InlineData("", "archivo")]
    [InlineData(null, "archivo")]
    [InlineData("...", "archivo")]
    public void Sanitizes_file_names(string? input, string expected) =>
        AttachmentRules.SanitizeFileName(input).Should().Be(expected);

    [Fact]
    public void Long_names_are_cut_keeping_the_extension()
    {
        var name = AttachmentRules.SanitizeFileName(new string('a', 400) + ".pdf");

        name.Should().HaveLength(AttachmentRules.MaxFileNameLength).And.EndWith(".pdf");
    }

    [Fact]
    public void Rejected_files_still_report_the_safe_name() =>
        AttachmentRules.Inspect("../x/virus.exe", WindowsExecutable).SafeFileName.Should().Be("virus.exe");

    private static void Assert(string name, byte[] content, string contentType)
    {
        var result = AttachmentRules.Inspect(name, content);

        result.IsValid.Should().BeTrue(result.ErrorMessage);
        result.ContentType.Should().Be(contentType);
    }

    private static void Reject(string name, byte[] content, string code)
    {
        var result = AttachmentRules.Inspect(name, content);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(code);
        result.ContentType.Should().BeNull();
    }
}
