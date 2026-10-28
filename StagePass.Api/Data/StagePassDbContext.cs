using Microsoft.EntityFrameworkCore;
using StagePass.Api.Entities;
using StagePass.Api.Enums;

namespace StagePass.Api.Data;

public class StagePassDbContext : DbContext
{
    public StagePassDbContext(DbContextOptions<StagePassDbContext> options) : base(options)
    {
    }

    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Organizer> Organizers => Set<Organizer>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Performance> Performances => Set<Performance>();
    public DbSet<TicketType> TicketTypes => Set<TicketType>();
    public DbSet<ReservationHold> ReservationHolds => Set<ReservationHold>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All DateTime values are UTC and stored as datetime2.
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Venue>(e =>
        {
            e.Property(v => v.Name).HasMaxLength(200).IsRequired();
            e.Property(v => v.Address).HasMaxLength(500).IsRequired();
            e.Property(v => v.City).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<Organizer>(e =>
        {
            e.Property(o => o.Name).HasMaxLength(200).IsRequired();
            e.Property(o => o.Email).HasMaxLength(254).IsRequired();
            e.HasIndex(o => o.Email).IsUnique();
        });

        modelBuilder.Entity<Event>(e =>
        {
            e.Property(ev => ev.Title).HasMaxLength(200).IsRequired();
            e.Property(ev => ev.Category).HasMaxLength(100).IsRequired();
            e.Property(ev => ev.Description).HasMaxLength(2000);

            e.HasOne(ev => ev.Organizer)
                .WithMany(o => o.Events)
                .HasForeignKey(ev => ev.OrganizerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(ev => ev.Venue)
                .WithMany(v => v.Events)
                .HasForeignKey(ev => ev.VenueId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Performance>(e =>
        {
            e.HasOne(p => p.Event)
                .WithMany(ev => ev.Performances)
                .HasForeignKey(p => p.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(p => p.Status)
                .HasConversion<string>()
                .HasColumnType("nvarchar(20)")
                .HasDefaultValue(PerformanceStatus.Upcoming);

            e.HasIndex(p => p.Status);
        });

        modelBuilder.Entity<TicketType>(e =>
        {
            e.Property(tt => tt.Name).HasMaxLength(100).IsRequired();
            e.Property(tt => tt.Price).HasColumnType("decimal(10,2)");

            e.HasOne(tt => tt.Performance)
                .WithMany(p => p.TicketTypes)
                .HasForeignKey(tt => tt.PerformanceId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(tt => new { tt.PerformanceId, tt.Name }).IsUnique();

            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_TicketType_Available_NonNeg", "[Available] >= 0");
                t.HasCheckConstraint("CK_TicketType_Available_Cap", "[Available] <= [Capacity]");
                t.HasCheckConstraint("CK_TicketType_Capacity_Pos", "[Capacity] > 0");
                t.HasCheckConstraint("CK_TicketType_Price_NonNeg", "[Price] >= 0");
            });
        });

        modelBuilder.Entity<ReservationHold>(e =>
        {
            // HoldId does not match the <Type>Id convention, so declare the key explicitly.
            e.HasKey(rh => rh.HoldId);

            e.Property(rh => rh.CustomerIdentifier).HasMaxLength(200).IsRequired();

            e.HasOne(rh => rh.TicketType)
                .WithMany(tt => tt.ReservationHolds)
                .HasForeignKey(rh => rh.TicketTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(rh => rh.IdempotencyKey).IsUnique();

            e.Property(rh => rh.Status)
                .HasConversion<string>()
                .HasColumnType("nvarchar(20)")
                .HasDefaultValue(ReservationStatus.Pending);

            e.Property(rh => rh.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            e.HasIndex(rh => rh.ExpiresAt)
                .HasFilter("[Status] = 'Pending'")
                .HasDatabaseName("IX_ReservationHolds_ExpiresAt_Pending");

            e.ToTable(t => t.HasCheckConstraint("CK_Hold_Quantity_Pos", "[Quantity] > 0"));
        });

        modelBuilder.Entity<Booking>(e =>
        {
            e.Property(b => b.BookingReference).HasMaxLength(20).IsRequired();
            e.Property(b => b.CustomerIdentifier).HasMaxLength(200).IsRequired();
            e.Property(b => b.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Confirmed");

            e.HasOne(b => b.ReservationHold)
                .WithOne(rh => rh.Booking)
                .HasForeignKey<Booking>(b => b.HoldId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(b => b.BookingReference).IsUnique();
            e.HasIndex(b => b.HoldId).IsUnique();
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampCreatedAt();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampCreatedAt();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Makes CreatedAt available immediately on the returned entity.
    private void StampCreatedAt()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
        {
            if (entry.Entity is ReservationHold hold && hold.CreatedAt == default)
                hold.CreatedAt = now;
            else if (entry.Entity is Booking booking && booking.CreatedAt == default)
                booking.CreatedAt = now;
        }
    }
}

