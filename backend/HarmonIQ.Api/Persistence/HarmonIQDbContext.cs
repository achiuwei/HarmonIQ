using HarmonIQ.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace HarmonIQ.Api.Persistence;

public class HarmonIQDbContext : DbContext
{
    public HarmonIQDbContext(DbContextOptions<HarmonIQDbContext> options) : base(options)
    {
    }

    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<InputSet> InputSets => Set<InputSet>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<Analysis> Analyses => Set<Analysis>();
    public DbSet<SubjectOrientation> SubjectOrientations => Set<SubjectOrientation>();
    public DbSet<EngineVersion> EngineVersions => Set<EngineVersion>();
    public DbSet<ScoringJob> ScoringJobs => Set<ScoringJob>();
    public DbSet<ProjectionRow> ProjectionRows => Set<ProjectionRow>();

    /// <summary>
    /// InputSet rows are immutable once written (design §5). Any attempt to modify a
    /// tracked InputSet entity is rejected here rather than at the SQLite layer, so the
    /// guard also protects providers (e.g. InMemory) without a real unique/trigger story.
    /// </summary>
    private void GuardInputSetImmutability()
    {
        var mutated = ChangeTracker.Entries<InputSet>().Any(e => e.State == EntityState.Modified);
        if (mutated)
        {
            throw new InvalidOperationException("InputSet rows are immutable once written; create a new InputSet instead of mutating an existing one.");
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardInputSetImmutability();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardInputSetImmutability();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Subject>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PropertyKey);
            e.HasIndex(x => x.ExternalPlanKey);
        });

        modelBuilder.Entity<InputSet>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SubjectId);
            e.HasIndex(x => x.InputFingerprint);
        });

        modelBuilder.Entity<Observation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SubjectId, x.EvidenceHash, x.PromptVersion, x.ModelId }).IsUnique();
        });

        modelBuilder.Entity<Analysis>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SubjectId, x.PrincipleSet, x.RulesVersion }).IsUnique();
        });

        modelBuilder.Entity<SubjectOrientation>(e =>
        {
            e.HasKey(x => x.SubjectId);
        });

        modelBuilder.Entity<EngineVersion>(e =>
        {
            e.HasKey(x => x.Version);
        });

        modelBuilder.Entity<ScoringJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SubjectId, x.EngineVersion });
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<ProjectionRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ListingId, x.FloorPlanId, x.PrincipleSet });
        });
    }
}
