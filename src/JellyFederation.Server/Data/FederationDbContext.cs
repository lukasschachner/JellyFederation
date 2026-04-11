using JellyFederation.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace JellyFederation.Server.Data;

public class FederationDbContext(DbContextOptions<FederationDbContext> options) : DbContext(options)
{
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
        });
    }
}
