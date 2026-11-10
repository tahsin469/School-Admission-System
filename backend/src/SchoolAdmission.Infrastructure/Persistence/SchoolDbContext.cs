using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Domain.Entities;

namespace SchoolAdmission.Infrastructure.Persistence;

public class SchoolDbContext : DbContext
{
    public SchoolDbContext(DbContextOptions<SchoolDbContext> options) : base(options) { }

    public DbSet<Class> Classes => Set<Class>();
    public DbSet<Seat> Seats => Set<Seat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Class Configuration
        modelBuilder.Entity<Class>(entity =>
        {
            entity.ToTable("Classes");
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(c => c.TotalSeats)
                .IsRequired();

            entity.Property(c => c.AdmissionFee)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(c => c.CreatedAtUtc)
                .IsRequired();

            // 1-to-Many Relationship: Class -> Seats
            entity.HasMany(c => c.Seats)
                .WithOne()
                .HasForeignKey(s => s.ClassId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Classes_TotalSeats_Positive", "\"TotalSeats\" > 0");
                t.HasCheckConstraint("CK_Classes_AdmissionFee_NonNegative", "\"AdmissionFee\" >= 0");
            });
        });

        // 2. Seat Configuration
        modelBuilder.Entity<Seat>(entity =>
        {
            entity.ToTable("Seats");
            entity.HasKey(s => s.Id);

            entity.Property(s => s.SeatNumber)
                .IsRequired();

            entity.Property(s => s.Status)
                .IsRequired()
                .HasConversion<int>();

            entity.Property(s => s.CurrentStudentId)
                .HasMaxLength(50);

            entity.Property(s => s.HeldAtUtc);
            entity.Property(s => s.ExpiresAtUtc);

            // Optimistic concurrency token
            entity.Property(s => s.Version)
                .IsConcurrencyToken();

            // Constraint: SeatNumber unique per Class
            entity.HasIndex(s => new { s.ClassId, s.SeatNumber })
                .IsUnique()
                .HasDatabaseName("IX_Seats_ClassId_SeatNumber");

            // Partial Unique Constraint: A student can only hold/confirm 1 seat per class
            // PostgreSQL filter syntax
            entity.HasIndex(s => new { s.ClassId, s.CurrentStudentId })
                .IsUnique()
                .HasFilter("\"CurrentStudentId\" IS NOT NULL AND \"Status\" IN (1, 2)")
                .HasDatabaseName("IX_Seats_Unique_Active_Student_Per_Class");

            // Partial Index for high-speed Background Sweeper queries
            entity.HasIndex(s => s.ExpiresAtUtc)
                .HasFilter("\"Status\" = 1")
                .HasDatabaseName("IX_Seats_ExpiresAtUtc_Held");
        });
    }
}
