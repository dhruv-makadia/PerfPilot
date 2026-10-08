using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PerfPilot.Core.Domain;

namespace PerfPilot.Infrastructure.Persistence;

public sealed class PerfPilotDbContext(DbContextOptions<PerfPilotDbContext> options) : DbContext(options)
{
    public DbSet<TargetEnvironment> Environments => Set<TargetEnvironment>();
    public DbSet<ApiSpec> ApiSpecs => Set<ApiSpec>();
    public DbSet<ApiEndpoint> ApiEndpoints => Set<ApiEndpoint>();
    public DbSet<Scenario> Scenarios => Set<Scenario>();
    public DbSet<TestRun> TestRuns => Set<TestRun>();
    public DbSet<RunEndpointResult> RunEndpointResults => Set<RunEndpointResult>();
    public DbSet<RunThresholdResult> RunThresholdResults => Set<RunThresholdResult>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<TargetEnvironment>(e =>
        {
            e.ToTable("Environments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.BaseUrl).HasMaxLength(2048).IsRequired();
            e.Property(x => x.OpenApiUrl).HasMaxLength(2048);
            e.Property(x => x.ApiKeyHeaderName).HasMaxLength(200);
            e.Property(x => x.LoginUrl).HasMaxLength(2048);
            e.Property(x => x.LoginTokenJsonPath).HasMaxLength(500);
            e.Property(x => x.LoginTokenHeaderName).HasMaxLength(200);
            e.Property(x => x.LoginTokenPrefix).HasMaxLength(100);
            e.HasMany(x => x.Specs).WithOne(s => s.Environment).HasForeignKey(s => s.EnvironmentId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ApiSpec>(e =>
        {
            e.ToTable("ApiSpecs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EnvironmentId, x.Version }).IsUnique();
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.ApiVersion).HasMaxLength(100);
            e.Property(x => x.SpecFormat).HasMaxLength(50);
            e.Property(x => x.SourceLocation).HasMaxLength(2048);
            e.Property(x => x.ImportedBy).HasMaxLength(200);
            e.Property(x => x.Warnings).HasJsonConversion();
            e.HasMany(x => x.Endpoints).WithOne().HasForeignKey(x => x.ApiSpecId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ApiEndpoint>(e =>
        {
            e.ToTable("ApiEndpoints");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ApiSpecId, x.SortOrder });
            e.Property(x => x.Method).HasMaxLength(10).IsRequired();
            e.Property(x => x.Path).HasMaxLength(2048).IsRequired();
            e.Property(x => x.OperationId).HasMaxLength(500);
            e.Property(x => x.Tag).HasMaxLength(200).IsRequired();
            e.Property(x => x.RequestBodyContentType).HasMaxLength(200);
            e.Property(x => x.Parameters).HasJsonConversion();
            e.Property(x => x.ResponseCodes).HasJsonConversion();
        });

        model.Entity<Scenario>(e =>
        {
            e.ToTable("Scenarios");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.Property(x => x.Definition).HasJsonConversion();
            e.HasOne(x => x.Environment).WithMany().HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApiSpec>().WithMany().HasForeignKey(x => x.ApiSpecId).OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<TestRun>(e =>
        {
            e.ToTable("TestRuns");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.QueuedAt);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.ScenarioId, x.QueuedAt });
            e.HasIndex(x => new { x.EnvironmentId, x.QueuedAt });
            e.Property(x => x.ScenarioName).HasMaxLength(150).IsRequired();
            e.Property(x => x.EnvironmentName).HasMaxLength(100).IsRequired();
            e.Property(x => x.EnvironmentBaseUrl).HasMaxLength(2048).IsRequired();
            e.Property(x => x.StartedBy).HasMaxLength(200).IsRequired();
            e.Property(x => x.CancelledBy).HasMaxLength(200);
            e.Property(x => x.WorkDirectory).HasMaxLength(1024);
            e.Property(x => x.ScenarioSnapshot).HasJsonConversion();
            e.Property(x => x.ThresholdPlan).HasJsonConversion();
            e.Property(x => x.TimeSeries).HasJsonConversion();
            e.OwnsOne(x => x.Overall, ConfigureMetrics);
            e.Navigation(x => x.Overall).IsRequired();
            e.HasOne<Scenario>().WithMany().HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<TargetEnvironment>().WithMany().HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.EndpointResults).WithOne().HasForeignKey(x => x.TestRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ThresholdResults).WithOne().HasForeignKey(x => x.TestRunId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.IsFinished);
        });

        model.Entity<RunEndpointResult>(e =>
        {
            e.ToTable("RunEndpointResults");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Key).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(2200).IsRequired();
            e.Property(x => x.Method).HasMaxLength(10).IsRequired();
            e.Property(x => x.Path).HasMaxLength(2048).IsRequired();
            e.OwnsOne(x => x.Metrics, ConfigureMetrics);
            e.Navigation(x => x.Metrics).IsRequired();
        });

        model.Entity<RunThresholdResult>(e =>
        {
            e.ToTable("RunThresholdResults");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.EndpointKey).HasMaxLength(20);
            e.Property(x => x.ScopeName).HasMaxLength(2200).IsRequired();
            e.Property(x => x.K6Metric).HasMaxLength(200).IsRequired();
            e.Property(x => x.Expression).HasMaxLength(100).IsRequired();
        });
    }

    private static void ConfigureMetrics<TOwner>(OwnedNavigationBuilder<TOwner, RunMetrics> m) where TOwner : class
    {
        m.Property(x => x.Count).HasColumnName("Count");
        m.Property(x => x.Failed).HasColumnName("FailedCount");
        m.Property(x => x.AvgMs).HasColumnName("AvgMs");
        m.Property(x => x.MinMs).HasColumnName("MinMs");
        m.Property(x => x.P50Ms).HasColumnName("P50Ms");
        m.Property(x => x.P90Ms).HasColumnName("P90Ms");
        m.Property(x => x.P95Ms).HasColumnName("P95Ms");
        m.Property(x => x.P99Ms).HasColumnName("P99Ms");
        m.Property(x => x.MaxMs).HasColumnName("MaxMs");
        m.Property(x => x.ErrorRate).HasColumnName("ErrorRate");
        m.Property(x => x.Rps).HasColumnName("Rps");
    }
}
