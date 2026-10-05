using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Helpdesk.Contracts.Tickets;
using Helpdesk.IntegrationTests.Infrastructure;

namespace Helpdesk.IntegrationTests;

/// <summary>Adjuntos validados por firma, guardados fuera de la base y descargados solo con autorización.</summary>
public sealed class AttachmentTests(HelpdeskFixture fixture)
{
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF\n"u8.ToArray()];
    private static readonly byte[] Executable = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00];

    [Fact]
    public async Task A_valid_pdf_is_stored_and_downloaded_as_an_attachment()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var ticket = await customer.CreateTicketAsync();

        var uploaded = await (await Upload(customer, ticket.Id, "../../estado de cuenta.pdf", Pdf)).ReadAsync<AttachmentDto>(HttpStatusCode.Created);
        var download = await customer.GetAsync($"/api/v1/attachments/{uploaded.Id}/content");

        uploaded.FileName.Should().Be("estado de cuenta.pdf");
        uploaded.ContentType.Should().Be("application/pdf");
        uploaded.SizeBytes.Should().Be(Pdf.Length);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        download.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("sandbox");
        download.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        Directory.EnumerateFiles(fixture.StoragePath, "*", SearchOption.AllDirectories)
            .Should().Contain(f => Path.GetFileName(f).Length == 32 && !Path.HasExtension(f), "el archivo se guarda con nombre aleatorio sin extensión");
    }

    [Fact]
    public async Task An_executable_renamed_as_pdf_is_rejected()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var ticket = await customer.CreateTicketAsync();

        var response = await Upload(customer, ticket.Id, "factura.pdf", Executable);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ProblemCodeAsync()).Should().Be("attachment_signature_mismatch");
    }

    [Fact]
    public async Task Disallowed_types_and_oversized_files_are_rejected()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var ticket = await customer.CreateTicketAsync();
        var big = new byte[(5 * 1024 * 1024) + 10];
        Pdf.CopyTo(big, 0);

        var html = await Upload(customer, ticket.Id, "pagina.html", Encoding.UTF8.GetBytes("<script>alert(1)</script>"));
        var oversized = await Upload(customer, ticket.Id, "grande.pdf", big);

        html.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await html.ProblemCodeAsync()).Should().Be("attachment_type_not_allowed");
        oversized.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Internal_attachments_are_invisible_to_customers()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var agent = await fixture.ClientAsAsync("agente");
        var ticket = await customer.CreateTicketAsync();

        var internalFile = await (await Upload(agent, ticket.Id, "analisis.txt", "Revisión interna del caso"u8.ToArray(), isInternal: true)).ReadAsync<AttachmentDto>(HttpStatusCode.Created);
        var customerTriesInternal = await Upload(customer, ticket.Id, "x.txt", "hola"u8.ToArray(), isInternal: true);

        customerTriesInternal.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.GetTicketAsync(ticket.Id)).Attachments.Should().NotContain(a => a.Id == internalFile.Id);
        (await customer.GetAsync($"/api/v1/attachments/{internalFile.Id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await agent.GetAsync($"/api/v1/attachments/{internalFile.Id}/content")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Other_customers_cannot_download_the_file()
    {
        var customer = await fixture.CustomerWithConsentAsync();
        var ticket = await customer.CreateTicketAsync();
        var uploaded = await (await Upload(customer, ticket.Id, "captura.pdf", Pdf)).ReadAsync<AttachmentDto>(HttpStatusCode.Created);
        var stranger = await fixture.ClientAsAsync("luis.cardenas@correo.example");

        (await stranger.GetAsync($"/api/v1/attachments/{uploaded.Id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Upload(stranger, ticket.Id, "x.pdf", Pdf)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> Upload(HttpClient client, Guid ticketId, string fileName, byte[] content, bool isInternal = false)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(isInternal ? "true" : "false"), "isInternal");
        return client.PostAsync($"/api/v1/tickets/{ticketId}/attachments", form);
    }
}
