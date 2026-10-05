using Helpdesk.Domain.Catalog;
using Helpdesk.Domain.Common;
using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Notifications;
using Helpdesk.Domain.Sla;
using Helpdesk.Domain.Tickets;
using Helpdesk.Infrastructure.Identity;
using Helpdesk.Infrastructure.Persistence.Converters;
using Helpdesk.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Infrastructure.Persistence;

/// <summary>Contexto único de la mesa de ayuda: dominio + Identity, en PostgreSQL con nombres snake_case.</summary>
public sealed class HelpdeskDbContext(DbContextOptions<HelpdeskDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public const string TicketNumberSequence = "ticket_number_seq";

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<TicketComment> TicketComments => Set<TicketComment>();

    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();

    public DbSet<TicketStatusChange> TicketStatusHistory => Set<TicketStatusChange>();

    public DbSet<SatisfactionSurvey> SatisfactionSurveys => Set<SatisfactionSurvey>();

    public DbSet<TicketSuggestion> TicketSuggestions => Set<TicketSuggestion>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<BusinessCalendar> BusinessCalendars => Set<BusinessCalendar>();

    public DbSet<Holiday> Holidays => Set<Holiday>();

    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ApiClient> ApiClients => Set<ApiClient>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        // Las tablas de Identity se renombran antes de configurar las relaciones que apuntan a ellas,
        // así los nombres de las FK salen de "users" y no del nombre por defecto.
        ConfigureIdentityTables(builder);
        builder.HasSequence<long>(TicketNumberSequence).StartsAt(1).IncrementsBy(1);
        builder.ApplyConfigurationsFromAssembly(typeof(HelpdeskDbContext).Assembly);
        ReferenceData.Seed(builder);

        foreach (var entityType in builder.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            builder.Entity(entityType.ClrType).Ignore(nameof(Entity.DomainEvents));
        }
    }

    private static void ConfigureIdentityTables(ModelBuilder builder)
    {
        builder.Entity<AppUser>(user =>
        {
            user.ToTable("users");
            user.Property(u => u.FullName).HasMaxLength(AppUser.FullNameMaxLength).IsRequired();
            user.Property(u => u.PrivacyNoticeVersion).HasMaxLength(20);
            user.HasOne<Customer>().WithMany().HasForeignKey(u => u.CustomerId).OnDelete(DeleteBehavior.Restrict);
            user.HasIndex(u => u.CustomerId);
        });
        builder.Entity<AppRole>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
    }
}
