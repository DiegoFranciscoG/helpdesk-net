using Helpdesk.Domain.Catalog;
using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Notifications;
using Helpdesk.Domain.Sla;
using Helpdesk.Domain.Tickets;
using Helpdesk.Infrastructure.Identity;
using Helpdesk.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helpdesk.Infrastructure.Persistence.Configurations;

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(40).IsRequired();
        builder.HasIndex(t => t.Code).IsUnique();
        builder.Property(t => t.Name).HasMaxLength(80).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(300).IsRequired();
        builder.HasMany(t => t.Members).WithOne().HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("team_members");
        builder.HasKey(m => new { m.TeamId, m.UserId });
        builder.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => m.UserId);
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).HasMaxLength(40).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();
        builder.Property(c => c.Name).HasMaxLength(80).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(300).IsRequired();
        builder.HasOne<Team>().WithMany().HasForeignKey(c => c.DefaultTeamId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers", t =>
        {
            t.HasCheckConstraint("ck_customers_kind", $"kind IN ({SnakeCaseEnumConverter<CustomerKind>.SqlList})");
            t.HasCheckConstraint("ck_customers_service_tier", $"service_tier IN ({SnakeCaseEnumConverter<ServiceTier>.SqlList})");
        });
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();
        builder.Property(c => c.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Kind).HasConversion<SnakeCaseEnumConverter<CustomerKind>>().HasMaxLength(20);
        builder.Property(c => c.ServiceTier).HasConversion<SnakeCaseEnumConverter<ServiceTier>>().HasMaxLength(20);
        builder.Property(c => c.Email).HasMaxLength(254);
        builder.Property(c => c.Phone).HasMaxLength(20);
    }
}

internal sealed class BusinessCalendarConfiguration : IEntityTypeConfiguration<BusinessCalendar>
{
    public void Configure(EntityTypeBuilder<BusinessCalendar> builder)
    {
        builder.ToTable("business_calendars");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).HasMaxLength(40).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();
        builder.Property(c => c.Name).HasMaxLength(80).IsRequired();
        builder.Property(c => c.TimeZoneId).HasColumnName("time_zone").HasMaxLength(64).IsRequired();
        builder.Property(c => c.Is24x7).HasColumnName("is_24x7");
        builder.HasMany(c => c.Hours).WithOne().HasForeignKey(h => h.CalendarId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.Holidays).WithOne().HasForeignKey(h => h.CalendarId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BusinessHoursConfiguration : IEntityTypeConfiguration<BusinessHours>
{
    public void Configure(EntityTypeBuilder<BusinessHours> builder)
    {
        builder.ToTable("business_hours", t =>
        {
            t.HasCheckConstraint("ck_business_hours_day", "day_of_week BETWEEN 1 AND 7");
            t.HasCheckConstraint("ck_business_hours_range", "end_time > start_time");
        });
        builder.HasKey(h => h.Id);
        builder.Property(h => h.DayOfWeek).HasConversion<IsoDayOfWeekConverter>();
        builder.HasIndex(h => new { h.CalendarId, h.DayOfWeek, h.StartTime }).IsUnique();
    }
}

internal sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        builder.ToTable("holidays");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Name).HasMaxLength(120).IsRequired();
        builder.Property(h => h.LegalBasis).HasMaxLength(200).IsRequired();
        builder.HasIndex(h => new { h.CalendarId, h.Date }).IsUnique();
    }
}

internal sealed class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> builder)
    {
        builder.ToTable("sla_policies", t =>
        {
            t.HasCheckConstraint("ck_sla_policies_priority", "priority BETWEEN 1 AND 5");
            t.HasCheckConstraint("ck_sla_policies_service_tier", $"service_tier IS NULL OR service_tier IN ({SnakeCaseEnumConverter<ServiceTier>.SqlList})");
            t.HasCheckConstraint("ck_sla_policies_first_response", "first_response_minutes > 0");
            t.HasCheckConstraint("ck_sla_policies_resolution", "resolution_minutes >= first_response_minutes");
            t.HasCheckConstraint("ck_sla_policies_warning", $"warning_percent BETWEEN {SlaPolicy.MinWarningPercent} AND {SlaPolicy.MaxWarningPercent}");
        });
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(80).IsRequired();
        builder.Property(p => p.Priority).HasConversion<short>();
        builder.Property(p => p.ServiceTier).HasConversion<SnakeCaseEnumConverter<ServiceTier>>().HasMaxLength(20);
        builder.Property(p => p.WarningPercent).HasConversion<short>();
        builder.HasOne<BusinessCalendar>().WithMany().HasForeignKey(p => p.CalendarId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.Priority, p.ServiceTier }).IsUnique().AreNullsDistinct(false);
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", t =>
            t.HasCheckConstraint("ck_notifications_type", $"type IN ({SnakeCaseEnumConverter<NotificationType>.SqlList})"));
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Type).HasConversion<SnakeCaseEnumConverter<NotificationType>>().HasMaxLength(40);
        builder.Property(n => n.Title).HasMaxLength(Notification.TitleMaxLength).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(Notification.MessageMaxLength).IsRequired();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(n => n.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.HasIndex(r => r.TokenHash).IsUnique();
        builder.HasIndex(r => r.FamilyId);
        builder.Property(r => r.RevokedReason).HasMaxLength(40);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ApiClientConfiguration : IEntityTypeConfiguration<ApiClient>
{
    public void Configure(EntityTypeBuilder<ApiClient> builder)
    {
        builder.ToTable("api_clients");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ClientId).HasMaxLength(64).IsRequired();
        builder.HasIndex(c => c.ClientId).IsUnique();
        builder.Property(c => c.DisplayName).HasMaxLength(80).IsRequired();
        builder.Property(c => c.SecretHash).IsRequired();
    }
}
