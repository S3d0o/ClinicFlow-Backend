using Microsoft.EntityFrameworkCore.Metadata;

namespace Persistence.Auditing
{
    // How a property of an audited type is written to its audit entries
    public enum AuditMode
    {
        Recorded, // old and new values
        Redacted, // only that it changed: clinical values are never copied into the audit (08 §4)
        Ignored,  // not audited; a change to ignored properties only writes no entry
    }

    // Marks what the audit interceptor records, in each entity's configuration, so a new entity type opts in where it
    // is configured. Stored as model annotations; AuditedType reads them back. AuditModelTests list the audited types
    public static class AuditModelBuilderExtensions
    {
        internal const string NameAnnotation = "ClinicFlow:Audit:Name";
        internal const string DefaultModeAnnotation = "ClinicFlow:Audit:DefaultMode";
        internal const string ModeAnnotation = "ClinicFlow:Audit:Mode";

        // Audits every create, update and delete of T under `name`. Properties are recorded unless marked otherwise;
        // with defaultMode Ignored, only the properties marked IsAudited() are
        public static EntityTypeBuilder<T> IsAudited<T>(
            this EntityTypeBuilder<T> builder, string name, AuditMode defaultMode = AuditMode.Recorded) where T : class
            => builder
                .HasAnnotation(NameAnnotation, name)
                .HasAnnotation(DefaultModeAnnotation, defaultMode.ToString());

        public static PropertyBuilder<T> IsAudited<T>(this PropertyBuilder<T> builder)
            => builder.HasAnnotation(ModeAnnotation, nameof(AuditMode.Recorded));

        public static PropertyBuilder<T> IsAuditRedacted<T>(this PropertyBuilder<T> builder)
            => builder.HasAnnotation(ModeAnnotation, nameof(AuditMode.Redacted));

        public static PropertyBuilder<T> IsNotAudited<T>(this PropertyBuilder<T> builder)
            => builder.HasAnnotation(ModeAnnotation, nameof(AuditMode.Ignored));
    }

    // An audited entity type as the model describes it
    public sealed class AuditedType
    {
        private readonly AuditMode _defaultMode;

        private AuditedType(string name, AuditMode defaultMode)
        {
            Name = name;
            _defaultMode = defaultMode;
        }

        public string Name { get; }

        // Null when the type isn't audited
        public static AuditedType? Of(IReadOnlyEntityType entityType)
            => entityType[AuditModelBuilderExtensions.NameAnnotation] is string name
                ? new AuditedType(name, Enum.Parse<AuditMode>((string)entityType[AuditModelBuilderExtensions.DefaultModeAnnotation]!))
                : null;

        public AuditMode ModeOf(IReadOnlyProperty property)
        {
            // A single key is the entry's EntityId; the organization is the entry's own; a concurrency token is noise.
            // A composite key (a user's role) is what the record is, so it is recorded
            if (property.FindContainingPrimaryKey() is { Properties.Count: 1 }
                || property.Name == nameof(IOrganizationOwned.OrganizationId)
                || property.IsConcurrencyToken)
                return AuditMode.Ignored;

            return property[AuditModelBuilderExtensions.ModeAnnotation] is string mode ? Enum.Parse<AuditMode>(mode) : _defaultMode;
        }
    }
}
