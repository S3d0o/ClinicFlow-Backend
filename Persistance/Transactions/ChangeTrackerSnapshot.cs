using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Persistence.Transactions
{
    // The change tracker as it was before a transaction attempt. Restore() undoes the attempt in memory once the
    // database has rolled it back, so a retry starts from the same state as the first try
    internal sealed class ChangeTrackerSnapshot
    {
        private readonly DbContext _context;
        private readonly List<EntrySnapshot> _entries;

        private ChangeTrackerSnapshot(DbContext context, List<EntrySnapshot> entries)
        {
            _context = context;
            _entries = entries;
        }

        public static ChangeTrackerSnapshot Take(DbContext context)
            => new(context, context.ChangeTracker.Entries().Select(EntrySnapshot.Of).ToList());

        public void Restore()
        {
            var known = new HashSet<object>(_entries.Select(e => e.Entity), ReferenceEqualityComparer.Instance);

            // Entities first tracked during the attempt are forgotten, with any key the database generated for them,
            // so the operation can add them again
            foreach (var entry in _context.ChangeTracker.Entries().Where(e => !known.Contains(e.Entity)).ToList())
            {
                entry.State = EntityState.Detached;
                ResetGeneratedKeys(entry);
            }

            foreach (var snapshot in _entries)
                snapshot.Restore(_context);
        }

        // Identity column values: after a rollback they name rows that don't exist
        private static void ResetGeneratedKeys(EntityEntry entry)
        {
            foreach (var key in entry.Metadata.FindPrimaryKey()?.Properties ?? [])
            {
                if (key.GetValueGenerationStrategy() != SqlServerValueGenerationStrategy.IdentityColumn)
                    continue;

                var value = key.ClrType.IsValueType ? Activator.CreateInstance(key.ClrType) : null;
                if (key.PropertyInfo is { CanWrite: true } property)
                    property.SetValue(entry.Entity, value);
                else
                    key.FieldInfo?.SetValue(entry.Entity, value);
            }
        }

        private static bool IsGeneratedKey(IProperty property)
            => property.IsPrimaryKey() && property.GetValueGenerationStrategy() == SqlServerValueGenerationStrategy.IdentityColumn;

        private sealed record EntrySnapshot(
            object Entity, EntityState State, PropertyValues Current, PropertyValues? Original, HashSet<string> Modified)
        {
            public static EntrySnapshot Of(EntityEntry entry) => new(
                entry.Entity,
                entry.State,
                entry.CurrentValues.Clone(),
                entry.State == EntityState.Added ? null : entry.OriginalValues.Clone(),
                entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToHashSet());

            public void Restore(DbContext context)
            {
                var entry = context.Entry(Entity);

                if (State == EntityState.Added)
                {
                    // Saved during the attempt: track it as new again. Its temporary key isn't a real value, so the
                    // key is left to be generated again
                    entry.State = EntityState.Detached;
                    ResetGeneratedKeys(entry);
                    entry = context.Entry(Entity);
                    foreach (var property in Current.Properties.Where(p => !IsGeneratedKey(p)))
                        entry.Property(property.Name).CurrentValue = Current[property];
                    entry.State = EntityState.Added;
                    return;
                }

                // Deleted during the attempt, and detached by SaveChanges: track it again
                if (entry.State == EntityState.Detached)
                    entry.State = EntityState.Unchanged;

                entry.CurrentValues.SetValues(Current);
                entry.OriginalValues.SetValues(Original!);
                entry.State = State;

                if (State == EntityState.Modified)
                    foreach (var property in entry.Properties)
                        property.IsModified = Modified.Contains(property.Metadata.Name);
            }
        }
    }
}
