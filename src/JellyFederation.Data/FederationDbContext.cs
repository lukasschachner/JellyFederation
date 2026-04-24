using JellyFederation.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Data;

public class FederationDbContext : DbContext
{
    public FederationDbContext(DbContextOptions<FederationDbContext> options) : base(options)
    {
    }

    public DbSet<RegisteredServer> Servers => Set<RegisteredServer>();
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<FileRequest> FileRequests => Set<FileRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RegisteredServer>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.ApiKey).IsUnique();
            e.HasMany(s => s.MediaItems)
                .WithOne(m => m.Server)
                .HasForeignKey(m => m.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(s => s.SentInvitations)
                .WithOne(i => i.FromServer)
                .HasForeignKey(i => i.FromServerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(s => s.ReceivedInvitations)
                .WithOne(i => i.ToServer)
                .HasForeignKey(i => i.ToServerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MediaItem>(e =>
        {
            e.HasIndex(m => new { m.ServerId, m.Type });
            e.HasIndex(m => new { m.ServerId, m.Title });
        });

        modelBuilder.Entity<Invitation>(e =>
        {
            e.HasIndex(i => new { i.FromServerId, i.Status });
            e.HasIndex(i => new { i.ToServerId, i.Status });
        });

        modelBuilder.Entity<FileRequest>(e =>
        {
            e.HasOne(r => r.RequestingServer)
                .WithMany()
                .HasForeignKey(r => r.RequestingServerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.OwningServer)
                .WithMany()
                .HasForeignKey(r => r.OwningServerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(r => new { r.RequestingServerId, r.Status });
            e.HasIndex(r => new { r.OwningServerId, r.Status });
            e.HasIndex(r => new { r.Status, r.CreatedAt });
        });
    }
}
