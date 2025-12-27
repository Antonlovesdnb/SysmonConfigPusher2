using Microsoft.EntityFrameworkCore;
using SysmonConfigPusher.Core.Models;

namespace SysmonConfigPusher.Data;

public class SysmonDbContext : DbContext
{
    public SysmonDbContext(DbContextOptions<SysmonDbContext> options) : base(options)
    {
    }

    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<ComputerGroup> ComputerGroups => Set<ComputerGroup>();
    public DbSet<ComputerGroupMember> ComputerGroupMembers => Set<ComputerGroupMember>();
    public DbSet<Config> Configs => Set<Config>();
    public DbSet<DeploymentJob> DeploymentJobs => Set<DeploymentJob>();
    public DbSet<DeploymentResult> DeploymentResults => Set<DeploymentResult>();
    public DbSet<NoiseAnalysisRun> NoiseAnalysisRuns => Set<NoiseAnalysisRun>();
    public DbSet<NoiseResult> NoiseResults => Set<NoiseResult>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Computer
        modelBuilder.Entity<Computer>(entity =>
        {
            entity.HasIndex(e => e.Hostname).IsUnique();
        });

        // ComputerGroupMember - composite key
        modelBuilder.Entity<ComputerGroupMember>(entity =>
        {
            entity.HasKey(e => new { e.GroupId, e.ComputerId });

            entity.HasOne(e => e.Group)
                .WithMany(g => g.Members)
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Computer)
                .WithMany(c => c.GroupMemberships)
                .HasForeignKey(e => e.ComputerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // DeploymentJob
        modelBuilder.Entity<DeploymentJob>(entity =>
        {
            entity.HasOne(e => e.Config)
                .WithMany(c => c.DeploymentJobs)
                .HasForeignKey(e => e.ConfigId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // DeploymentResult
        modelBuilder.Entity<DeploymentResult>(entity =>
        {
            entity.HasOne(e => e.Job)
                .WithMany(j => j.Results)
                .HasForeignKey(e => e.JobId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Computer)
                .WithMany(c => c.DeploymentResults)
                .HasForeignKey(e => e.ComputerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // NoiseAnalysisRun
        modelBuilder.Entity<NoiseAnalysisRun>(entity =>
        {
            entity.HasOne(e => e.Computer)
                .WithMany()
                .HasForeignKey(e => e.ComputerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // NoiseResult
        modelBuilder.Entity<NoiseResult>(entity =>
        {
            entity.HasOne(e => e.Run)
                .WithMany(r => r.Results)
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
