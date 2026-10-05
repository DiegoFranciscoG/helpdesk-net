using Helpdesk.Domain.Catalog;
using Helpdesk.Domain.Customers;
using Helpdesk.Domain.Sla;
using Helpdesk.Domain.Tickets;
using Helpdesk.Infrastructure.Identity;
using Helpdesk.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helpdesk.Infrastructure.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets", t =>
        {
            t.HasCheckConstraint("ck_tickets_status", $"status IN ({SnakeCaseEnumConverter<TicketStatus>.SqlList})");
            t.HasCheckConstraint("ck_tickets_channel", $"channel IN ({SnakeCaseEnumConverter<TicketChannel>.SqlList})");
            t.HasCheckConstraint("ck_tickets_impact", "impact BETWEEN 1 AND 3");
            t.HasCheckConstraint("ck_tickets_urgency", "urgency BETWEEN 1 AND 3");
            t.HasCheckConstraint("ck_tickets_priority_matrix", "priority = impact + urgency - 1");
            t.HasCheckConstraint("ck_tickets_escalation_level", "escalation_level BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_tickets_reopen_count", "reopen_count >= 0");
            t.HasCheckConstraint("ck_tickets_paused_minutes", "sla_paused_business_minutes >= 0");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Number)
            .HasDefaultValueSql($"nextval('{HelpdeskDbContext.TicketNumberSequence}')")
            .ValueGeneratedOnAdd();
        builder.HasIndex(t => t.Number).IsUnique();

        builder.Property(t => t.Title).HasMaxLength(Ticket.TitleMaxLength).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(Ticket.DescriptionMaxLength).IsRequired();
        builder.Property(t => t.Status).HasConversion<SnakeCaseEnumConverter<TicketStatus>>().HasMaxLength(20);
        builder.Property(t => t.Channel).HasConversion<SnakeCaseEnumConverter<TicketChannel>>().HasMaxLength(20);
        builder.Property(t => t.Impact).HasConversion<short>();
        builder.Property(t => t.Urgency).HasConversion<short>();
        builder.Property(t => t.Priority).HasConversion<short>();
        builder.Property(t => t.EscalationLevel).HasConversion<short>();
        builder.Property(t => t.ReopenCount).HasConversion<short>();
        builder.Property(t => t.ResolutionSummary).HasMaxLength(Ticket.SummaryMaxLength);
        builder.Property(t => t.Version).IsRowVersion();
        builder.Ignore(t => t.DisplayNumber);

        builder.HasOne<Team>().WithMany().HasForeignKey(t => t.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SlaPolicy>().WithMany().HasForeignKey(t => t.SlaPolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.CreatedById).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.History).WithOne().HasForeignKey(h => h.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Comments).WithOne().HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Attachments).WithOne().HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(t => t.Survey).WithOne().HasForeignKey<SatisfactionSurvey>(s => s.TicketId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.TeamId, t.Status });
        builder.HasIndex(t => new { t.AssigneeId, t.Status });
        builder.HasIndex(t => new { t.CustomerId, t.CreatedAt });
        builder.HasIndex(t => t.CreatedAt);

        // Índices parciales para el monitor de SLA: solo tickets abiertos con avisos pendientes.
        builder.HasIndex(t => t.FirstResponseWarningAt)
            .HasDatabaseName("ix_tickets_pending_first_response")
            .HasFilter("first_responded_at IS NULL AND status NOT IN ('closed', 'cancelled')");
        builder.HasIndex(t => t.ResolutionWarningAt)
            .HasDatabaseName("ix_tickets_running_resolution")
            .HasFilter("status IN ('new', 'assigned', 'in_progress')");
        builder.HasIndex(t => t.AutoCloseAt)
            .HasDatabaseName("ix_tickets_auto_close")
            .HasFilter("status = 'resolved'");
    }
}

internal sealed class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.ToTable("ticket_comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Body).HasMaxLength(TicketComment.BodyMaxLength).IsRequired();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.TicketId, c.CreatedAt });
    }
}

internal sealed class TicketAttachmentConfiguration : IEntityTypeConfiguration<TicketAttachment>
{
    public void Configure(EntityTypeBuilder<TicketAttachment> builder)
    {
        builder.ToTable("ticket_attachments", t =>
        {
            t.HasCheckConstraint("ck_ticket_attachments_size", $"size_bytes BETWEEN 1 AND {Domain.Attachments.AttachmentRules.MaxBytes}");
        });
        builder.HasKey(a => a.Id);
        builder.Property(a => a.OriginalFileName).HasMaxLength(Domain.Attachments.AttachmentRules.MaxFileNameLength).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(a => a.StorageKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(a => a.StorageKey).IsUnique();
        builder.HasIndex(a => a.TicketId);
        builder.HasOne<TicketComment>().WithMany().HasForeignKey(a => a.CommentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(a => a.UploadedById).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TicketStatusChangeConfiguration : IEntityTypeConfiguration<TicketStatusChange>
{
    public void Configure(EntityTypeBuilder<TicketStatusChange> builder)
    {
        builder.ToTable("ticket_status_history", t =>
        {
            var statuses = SnakeCaseEnumConverter<TicketStatus>.SqlList;
            t.HasCheckConstraint("ck_ticket_status_history_from", $"from_status IS NULL OR from_status IN ({statuses})");
            t.HasCheckConstraint("ck_ticket_status_history_to", $"to_status IN ({statuses})");
        });
        builder.HasKey(h => h.Id);
        builder.Property(h => h.FromStatus).HasConversion<SnakeCaseEnumConverter<TicketStatus>>().HasMaxLength(20);
        builder.Property(h => h.ToStatus).HasConversion<SnakeCaseEnumConverter<TicketStatus>>().HasMaxLength(20);
        builder.Property(h => h.Reason).HasMaxLength(Ticket.ReasonMaxLength);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(h => h.ChangedById).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(h => new { h.TicketId, h.ChangedAt });
    }
}

internal sealed class SatisfactionSurveyConfiguration : IEntityTypeConfiguration<SatisfactionSurvey>
{
    public void Configure(EntityTypeBuilder<SatisfactionSurvey> builder)
    {
        builder.ToTable("satisfaction_surveys", t => t.HasCheckConstraint("ck_satisfaction_surveys_rating", "rating BETWEEN 1 AND 5"));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Rating).HasConversion<short>();
        builder.Property(s => s.Comment).HasMaxLength(SatisfactionSurvey.CommentMaxLength);
        builder.HasIndex(s => s.TicketId).IsUnique();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.SubmittedById).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(s => s.IsSatisfied);
    }
}

internal sealed class TicketSuggestionConfiguration : IEntityTypeConfiguration<TicketSuggestion>
{
    public void Configure(EntityTypeBuilder<TicketSuggestion> builder)
    {
        builder.ToTable("ticket_suggestions", t =>
        {
            t.HasCheckConstraint("ck_ticket_suggestions_status", $"status IN ({SnakeCaseEnumConverter<SuggestionStatus>.SqlList})");
            t.HasCheckConstraint("ck_ticket_suggestions_source", $"source IN ({SnakeCaseEnumConverter<SuggestionSource>.SqlList})");
            t.HasCheckConstraint("ck_ticket_suggestions_urgency", "suggested_urgency IS NULL OR suggested_urgency BETWEEN 1 AND 3");
            t.HasCheckConstraint("ck_ticket_suggestions_confidence", "(category_confidence IS NULL OR category_confidence BETWEEN 0 AND 1) AND (team_confidence IS NULL OR team_confidence BETWEEN 0 AND 1)");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Source).HasConversion<SnakeCaseEnumConverter<SuggestionSource>>().HasMaxLength(20);
        builder.Property(s => s.Status).HasConversion<SnakeCaseEnumConverter<SuggestionStatus>>().HasMaxLength(20);
        builder.Property(s => s.Provider).HasMaxLength(60).IsRequired();
        builder.Property(s => s.ModelVersion).HasMaxLength(60).IsRequired();
        builder.Property(s => s.SuggestedCategoryCode).HasMaxLength(TicketSuggestion.CodeMaxLength);
        builder.Property(s => s.SuggestedTeamCode).HasMaxLength(TicketSuggestion.CodeMaxLength);
        builder.Property(s => s.SuggestedUrgency).HasConversion<short?>();
        builder.Property(s => s.FinalUrgency).HasConversion<short?>();
        builder.Property(s => s.CategoryConfidence).HasPrecision(4, 3);
        builder.Property(s => s.TeamConfidence).HasPrecision(4, 3);
        builder.Property(s => s.Decision).HasMaxLength(20);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(s => s.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Category>().WithMany().HasForeignKey(s => s.FinalCategoryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Team>().WithMany().HasForeignKey(s => s.FinalTeamId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.ReviewedById).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(s => new { s.TicketId, s.CreatedAt });
        builder.HasIndex(s => s.TicketId)
            .HasDatabaseName("ux_ticket_suggestions_one_pending")
            .IsUnique()
            .HasFilter("status = 'pending'");
    }
}
