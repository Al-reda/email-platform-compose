using EmailPlatform.Compose.Contracts;
using EmailPlatform.Shared;
using EmailPlatform.Shared.Clients;
using EmailPlatform.Shared.Contracts;

namespace EmailPlatform.Compose.Endpoints;

/// <summary>
/// Public-facing endpoints for the Compose Service.
/// Extracts ManagerId from X-Manager-Id header and delegates to Storage.
/// Enforces ownership on edits so manager A can't edit manager B's announcement.
/// </summary>
public static class ComposeEndpoints
{
    public const string ManagerIdHeader = "X-Manager-Id";

    public static IEndpointRouteBuilder MapComposeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/announcements").WithTags("Compose");
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id}", UpdateAsync);
        return routes;
    }

    // ---------------------------------------------------------------- POST

    private static async Task<IResult> CreateAsync(
        HttpContext ctx,
        ComposeCreateRequest body,
        IStorageClient storage,
        ILogger<ComposeMarker> log,
        CancellationToken ct)
    {
        if (!TryGetManagerId(ctx, out var managerId))
        {
            return Results.BadRequest(new { error = $"{ManagerIdHeader} header is required." });
        }

        if (string.IsNullOrWhiteSpace(body.Subject) ||
            string.IsNullOrWhiteSpace(body.Body) ||
            body.Recipients is null || body.Recipients.Count == 0)
        {
            return Results.BadRequest(new { error = "Missing required fields." });
        }

        var storageRequest = new CreateAnnouncementRequest
        {
            ManagerId = managerId,
            Subject = body.Subject,
            Body = body.Body,
            Recipients = body.Recipients,
            ScheduledFor = body.ScheduledFor
        };

        try
        {
            var created = await storage.CreateAsync(storageRequest, ct);
            log.LogInformation("Manager {ManagerId} created announcement {AnnouncementId}",
                managerId, created.AnnouncementId);
            return Results.Created($"/api/v1/announcements/{created.AnnouncementId}", created);
        }
        catch (StorageClientException ex) when (ex.StatusCode == 400)
        {
            // Bubble up Storage's validation error (e.g. "must fall on a Thursday").
            return Results.BadRequest(new { error = ExtractErrorMessage(ex.ResponseBody) ?? "Invalid request." });
        }
    }

    // ----------------------------------------------------------------- PUT

    private static async Task<IResult> UpdateAsync(
        HttpContext ctx,
        string id,
        UpdateAnnouncementRequest body,
        IStorageClient storage,
        ILogger<ComposeMarker> log,
        CancellationToken ct)
    {
        if (!TryGetManagerId(ctx, out var managerId))
        {
            return Results.BadRequest(new { error = $"{ManagerIdHeader} header is required." });
        }

        // Ownership check: fetch the existing announcement and verify it belongs
        // to this manager. Storage doesn't know about auth — this is the layer
        // that enforces "managers can only edit their own announcements".
        var existing = await storage.GetAsync(id, ct);
        if (existing is null) return Results.NotFound();
        if (existing.ManagerId != managerId)
        {
            log.LogWarning("Manager {ManagerId} attempted to edit announcement {AnnouncementId} owned by {OwnerId}",
                managerId, id, existing.ManagerId);
            // Return 404 rather than 403 to avoid leaking existence.
            return Results.NotFound();
        }

        try
        {
            var updated = await storage.UpdateAsync(id, body, ct);
            if (updated is null) return Results.NotFound();
            return Results.Ok(updated);
        }
        catch (StorageClientException ex) when (ex.StatusCode == 400)
        {
            return Results.BadRequest(new { error = ExtractErrorMessage(ex.ResponseBody) ?? "Invalid request." });
        }
        catch (StorageClientException ex) when (ex.StatusCode == 409)
        {
            // Storage says the announcement is no longer editable (it's past Pending).
            return Results.Conflict(new { error = "Cannot edit — announcement is no longer pending." });
        }
    }

    // ------------------------------------------------------- Helpers

    private static bool TryGetManagerId(HttpContext ctx, out string managerId)
    {
        if (ctx.Request.Headers.TryGetValue(ManagerIdHeader, out var values) &&
            !string.IsNullOrWhiteSpace(values.ToString()))
        {
            managerId = values.ToString();
            return true;
        }
        managerId = string.Empty;
        return false;
    }

    private static string? ExtractErrorMessage(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var err) ? err.GetString() : null;
        }
        catch { return null; }
    }

    // Marker class for ILogger<T> so log messages are tagged with the Compose namespace.
    public sealed class ComposeMarker { }
}
