using Microsoft.EntityFrameworkCore;
using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using WebAPI.Bentley;

namespace WebAPI.Bentley.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<CreateOrUpdateFormRequest> FormDatas { get; set; }

        private static byte[] NewRowVersion() => BitConverter.GetBytes(DateTime.UtcNow.Ticks);

        public override int SaveChanges()
        {
            UpdateRowVersions();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            UpdateRowVersions();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void UpdateRowVersions()
        {
            var entries = ChangeTracker.Entries<CreateOrUpdateFormRequest>()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

            foreach (var e in entries)
            {
                e.Entity.RowVersion = NewRowVersion();
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<CreateOrUpdateFormRequest>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Subject).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.IsDeleted).IsRequired();
                entity.Property(e => e.RowVersion).IsRowVersion();
                entity.HasQueryFilter(e => !e.IsDeleted);
            });
        }
    }
}
