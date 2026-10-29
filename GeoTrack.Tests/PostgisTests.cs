using GeoTrack.Web.Data;
using GeoTrack.Web.DTOs;
using GeoTrack.Web.Models;
using GeoTrack.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GeoTrack.Tests;

public sealed class RequiresPostgisFactAttribute : FactAttribute
{
    public const string EnvVar = "GEOTRACK_TEST_CONNECTION";

    public RequiresPostgisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
        {
            Skip = $"{EnvVar} ayarlı değil; gerçek PostGIS testi atlandı.";
        }
    }
}

public class PostgisTests
{
    private static GeoTrackDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<GeoTrackDbContext>()
            .UseNpgsql(connectionString, o => o.UseNetTopologySuite())
            .Options);

    private static GeoTrackDbContext CreateRealContext() =>
        CreateContext(Environment.GetEnvironmentVariable(RequiresPostgisFactAttribute.EnvVar)!);

    private static IncidentInput Input(string title, double lat, double lng) => new()
    {
        Title = title,
        Description = "Test amaçlı geçici kayıt, test sonunda silinir.",
        Category = IncidentCategory.Other,
        Latitude = lat,
        Longitude = lng
    };

    /// <summary>
    /// Bağlantı gerektirmez: yalnızca LINQ ifadesinin SQL'de ST_DWithin'e çevrildiğini doğrular.
    /// Bu bir PostGIS çalışma testi DEĞİLDİR; çalışma testi aşağıdaki [RequiresPostgisFact] testleridir.
    /// </summary>
    [Fact]
    public void NearbyQuery_IsTranslatedToStDWithin()
    {
        using var db = CreateContext("Host=localhost;Database=unused;Username=unused;Password=unused");
        var center = GeoFactory.CreatePoint(40.2, 28.9);

        var sql = db.Incidents
            .Where(i => i.Location.IsWithinDistance(center, 500))
            .ToQueryString();

        Assert.Contains("ST_DWithin", sql);
    }

    [RequiresPostgisFact]
    public async Task Nearby_ReturnsOnlyIncidentsWithinRadius_OrderedByDistance()
    {
        await using var db = CreateRealContext();
        var service = new IncidentService(db);
        var tag = "[TEST-" + Guid.NewGuid().ToString("N")[..8] + "]";
        var ids = new List<int>();

        try
        {
            // Gine Körfezi civarı (0,0): gerçek verilerle çakışmaz. 0.0009° enlem ≈ 100 m.
            ids.Add((await service.CreateAsync(Input(tag + " A", 0.0, 0.0))).Id);
            ids.Add((await service.CreateAsync(Input(tag + " B", 0.0009, 0.0))).Id);   // ~100 m
            ids.Add((await service.CreateAsync(Input(tag + " C", 0.009, 0.0))).Id);    // ~1000 m

            var nearby = await service.NearbyAsync(0.0, 0.0, 500);

            var mine = nearby.Where(x => x.Title.StartsWith(tag)).ToList();
            Assert.Equal(new[] { tag + " A", tag + " B" }, mine.Select(x => x.Title).ToArray());

            var b = mine.Single(x => x.Title == tag + " B");
            Assert.InRange(b.DistanceMeters!.Value, 95, 105);
        }
        finally
        {
            foreach (var id in ids) await service.DeleteAsync(id);
        }
    }

    [RequiresPostgisFact]
    public async Task List_FiltersBySearchPriorityStatusAndDate_AndSortsDeterministically()
    {
        await using var db = CreateRealContext();
        var service = new IncidentService(db);
        var tag = "[FILTER-" + Guid.NewGuid().ToString("N")[..8] + "]";
        var ids = new List<int>();
        var from = DateTime.UtcNow.AddMinutes(-1);
        try
        {
            var low = Input(tag + " Alpha", 3, 3);
            low.Priority = IncidentPriority.Low;
            ids.Add((await service.CreateAsync(low)).Id);
            var criticalB = Input(tag + " Bravo", 3.001, 3);
            criticalB.Priority = IncidentPriority.Critical;
            var createdB = await service.CreateAsync(criticalB);
            ids.Add(createdB.Id);
            criticalB.Status = IncidentStatus.InProgress;
            Assert.NotNull(await service.UpdateAsync(createdB.Id, criticalB));
            var criticalC = Input(tag + " Charlie", 3.002, 3);
            criticalC.Priority = IncidentPriority.Critical;
            var createdC = await service.CreateAsync(criticalC);
            ids.Add(createdC.Id);
            criticalC.Status = IncidentStatus.InProgress;
            Assert.NotNull(await service.UpdateAsync(createdC.Id, criticalC));
            criticalC.Status = IncidentStatus.Resolved;
            Assert.NotNull(await service.UpdateAsync(createdC.Id, criticalC));

            var filtered = await service.ListAsync(
                IncidentStatus.InProgress, null, 1, 100,
                priority: IncidentPriority.Critical, search: tag, fromUtc: from,
                toUtc: DateTime.UtcNow.AddMinutes(1), sort: "title");
            Assert.Single(filtered.Items);
            Assert.Equal(tag + " Bravo", filtered.Items[0].Title);

            var sorted = await service.ListAsync(null, null, 1, 100, search: tag, sort: "title");
            Assert.Equal(new[] { tag + " Alpha", tag + " Bravo", tag + " Charlie" }, sorted.Items.Select(x => x.Title));
        }
        finally
        {
            foreach (var id in ids) await service.DeleteAsync(id);
        }
    }

    [RequiresPostgisFact]
    public async Task Crud_RoundTrip_PersistsAndReturnsNullForUnknownId()
    {
        await using var db = CreateRealContext();
        var service = new IncidentService(db);
        var tag = "[TEST-" + Guid.NewGuid().ToString("N")[..8] + "]";
        int? id = null;

        try
        {
            var created = await service.CreateAsync(Input(tag, 0.5, 0.5));
            id = created.Id;
            Assert.Equal(0.5, created.Latitude, 6);
            Assert.Equal(0.5, created.Longitude, 6);

            var update = Input(tag + " güncel", 0.5, 0.5);
            update.Status = IncidentStatus.InProgress;
            var progressed = await service.UpdateAsync(created.Id, update);
            Assert.NotNull(progressed);
            update.Status = IncidentStatus.Resolved;
            var updated = await service.UpdateAsync(created.Id, update);
            Assert.NotNull(updated);
            Assert.Equal(IncidentStatus.Resolved, updated!.Status);

            Assert.Null(await service.GetAsync(int.MaxValue));
            Assert.Null(await service.UpdateAsync(int.MaxValue, Input(tag, 0.5, 0.5)));
            Assert.False(await service.DeleteAsync(int.MaxValue));
        }
        finally
        {
            if (id is { } existing) await service.DeleteAsync(existing);
        }
    }

    [RequiresPostgisFact]
    public async Task LocationColumn_HasGistIndex()
    {
        await using var db = CreateRealContext();

        var definitions = await db.Database
            .SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'incidents'")
            .ToListAsync();

        Assert.Contains(definitions, d => d.Contains("USING gist", StringComparison.OrdinalIgnoreCase));
    }

    [RequiresPostgisFact]
    public async Task IncidentQueries_EnforcePerUserOwnership_WithoutLosingLegacySharedRows()
    {
        await using var db = CreateRealContext();
        var service = new IncidentService(db);
        var tag = "[ISOLATION-" + Guid.NewGuid().ToString("N")[..8] + "]";
        var userA = new GeoTrack.Web.Models.ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"), UserName = tag + "a@example.test", NormalizedUserName = (tag + "a@example.test").ToUpperInvariant(),
            Email = tag + "a@example.test", NormalizedEmail = (tag + "a@example.test").ToUpperInvariant(), EmailConfirmed = true
        };
        var userB = new GeoTrack.Web.Models.ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"), UserName = tag + "b@example.test", NormalizedUserName = (tag + "b@example.test").ToUpperInvariant(),
            Email = tag + "b@example.test", NormalizedEmail = (tag + "b@example.test").ToUpperInvariant(), EmailConfirmed = true
        };
        db.Users.AddRange(userA, userB);
        await db.SaveChangesAsync();
        int? id = null;
        try
        {
            var created = await service.CreateAsync(Input(tag, 1, 1), userId: userA.Id);
            id = created.Id;
            var ownerResults = await service.ListAsync(null, null, 1, 100, userId: userA.Id);
            var otherResults = await service.ListAsync(null, null, 1, 100, userId: userB.Id);
            Assert.Contains(ownerResults.Items, x => x.Id == id);
            Assert.DoesNotContain(otherResults.Items, x => x.Id == id);
            Assert.Null(await service.GetAsync(id.Value, userId: userB.Id));
            Assert.Null(await service.UpdateAsync(id.Value, Input(tag + " no", 1, 1), userId: userB.Id));
            Assert.False(await service.DeleteAsync(id.Value, userId: userB.Id));
        }
        finally
        {
            if (id is { } existing) await service.DeleteAsync(existing, userId: userA.Id);
            db.Users.RemoveRange(userA, userB);
            await db.SaveChangesAsync();
        }
    }

    [RequiresPostgisFact]
    public async Task Workflow_AssignsActiveIdentityAndTracksResolutionAndHistory()
    {
        await using var db = CreateRealContext();
        var service = new IncidentService(db);
        var tag = "[WORKFLOW-" + Guid.NewGuid().ToString("N")[..8] + "]";
        var assignee = new GeoTrack.Web.Models.ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"), UserName = tag + "@example.test", NormalizedUserName = (tag + "@example.test").ToUpperInvariant(),
            Email = tag + "@example.test", NormalizedEmail = (tag + "@example.test").ToUpperInvariant(), EmailConfirmed = true, IsActive = true
        };
        db.Users.Add(assignee);
        await db.SaveChangesAsync();
        int? incidentId = null;
        try
        {
            var created = await service.CreateAsync(Input(tag, 5, 5), userId: assignee.Id);
            incidentId = created.Id;
            await Assert.ThrowsAsync<InvalidIncidentTransitionException>(() =>
                service.ApplyWorkflowAsync(created.Id, IncidentStatus.Resolved, null, true, true, assignee.Id));
            var assigned = await service.ApplyWorkflowAsync(created.Id, IncidentStatus.Assigned, assignee.Id, true, true, assignee.Id);
            Assert.NotNull(assigned);
            Assert.Equal(IncidentStatus.Assigned, assigned!.Status);
            Assert.Equal(assignee.Id, assigned.AssignedToUserId);
            var resolved = await service.ApplyWorkflowAsync(created.Id, IncidentStatus.Resolved, assignee.Id, true, true, assignee.Id);
            Assert.NotNull(resolved);
            Assert.Equal(IncidentStatus.Resolved, resolved!.Status);
            Assert.NotNull(resolved.ResolvedAt);
            Assert.Equal(4, await db.IncidentHistory.CountAsync(h => h.IncidentId == created.Id));
        }
        finally
        {
            if (incidentId is { } existing) await service.DeleteAsync(existing, userId: assignee.Id);
            db.Users.Remove(assignee);
            await db.SaveChangesAsync();
        }
    }
}
