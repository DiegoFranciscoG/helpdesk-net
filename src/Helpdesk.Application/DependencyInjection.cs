using FluentValidation;
using Helpdesk.Application.Abstractions;
using Helpdesk.Application.Attachments;
using Helpdesk.Application.Catalog;
using Helpdesk.Application.Events;
using Helpdesk.Application.Notifications;
using Helpdesk.Application.Reports;
using Helpdesk.Application.Sla;
using Helpdesk.Application.Suggestions;
using Helpdesk.Application.Tickets;
using Helpdesk.Contracts.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>(ServiceLifetime.Singleton);

        services.AddScoped<ISlaTermsProvider, SlaTermsProvider>();
        services.AddScoped<TicketEventDispatcher>();
        services.AddScoped<IDomainEventDispatcher>(sp => sp.GetRequiredService<TicketEventDispatcher>());
        services.AddScoped<TicketDetailsBuilder>();
        services.AddScoped<TicketService>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<SuggestionService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<ReportService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<SlaAdminService>();
        services.AddScoped<SlaMonitorService>();
        return services;
    }
}
