using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TreasuryKit.BuildingBlocks.Application.Abstractions;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Idempotency;

/// <summary>
/// Makes POST commands under /api safe to retry (plan §3 Idempotency). A request with an <c>Idempotency-Key</c> header
/// runs once per tenant and key; repeats get the stored response with <c>Idempotent-Replayed: true</c>. A reused key with a
/// different request gets 422; a repeat while the first is still running gets 409. Responses ≥ 500 are not stored, so
/// the client can retry. Keys expire after 24 hours.
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    public const int MaxKeyLength = 100;
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private const int MaxStoredBody = 1024 * 1024;

    public async Task InvokeAsync(HttpContext context, PlatformDbContext db, ICurrentTenant tenant, IClock clock)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || !context.Request.Path.StartsWithSegments("/api")
            || !context.Request.Headers.TryGetValue(HeaderName, out var values)
            || tenant.TenantId is not { } tenantId)
        {
            await next(context);
            return;
        }

        var key = values.ToString().Trim();
        if (key.Length is 0 or > MaxKeyLength)
        {
            await Problem(context, StatusCodes.Status400BadRequest, "Idempotency.InvalidKey", $"{HeaderName} must be 1–{MaxKeyLength} characters.");
            return;
        }

        var hash = await HashRequestAsync(context.Request);
        var now = clock.UtcNow;

        var existing = await db.IdempotencyRecords.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Key == key, context.RequestAborted);
        if (existing is not null && existing.CreatedAt < now - Retention)
        {
            db.IdempotencyRecords.Remove(existing);
            await db.SaveChangesAsync(context.RequestAborted);
            existing = null;
        }

        if (existing is null)
        {
            existing = new IdempotencyRecord { TenantId = tenantId, Key = key, RequestHash = hash, CreatedAt = now };
            db.IdempotencyRecords.Add(existing);
            try
            {
                await db.SaveChangesAsync(context.RequestAborted);
            }
            catch (DbUpdateException)
            {
                // Another request with the same key got there first.
                db.ChangeTracker.Clear();
                var winner = await db.IdempotencyRecords.AsNoTracking().FirstAsync(r => r.TenantId == tenantId && r.Key == key, context.RequestAborted);
                await RespondToRepeatAsync(context, winner, hash);
                return;
            }

            await ExecuteAndStoreAsync(context, db, existing, clock);
            return;
        }

        await RespondToRepeatAsync(context, existing, hash);
    }

    private async Task ExecuteAndStoreAsync(HttpContext context, PlatformDbContext db, IdempotencyRecord record, IClock clock)
    {
        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        catch
        {
            context.Response.Body = original;
            await ForgetAsync(db, record);
            throw;
        }

        context.Response.Body = original;
        buffer.Position = 0;
        await buffer.CopyToAsync(original, context.RequestAborted);

        if (context.Response.StatusCode >= 500)
        {
            await ForgetAsync(db, record); // let the client retry
            return;
        }

        record.CompletedAt = clock.UtcNow;
        record.StatusCode = context.Response.StatusCode;
        record.ContentType = context.Response.ContentType;
        record.Location = context.Response.Headers.Location.ToString() is { Length: > 0 } location ? location : null;
        record.ResponseBody = buffer.Length <= MaxStoredBody ? buffer.ToArray() : null;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task RespondToRepeatAsync(HttpContext context, IdempotencyRecord record, string hash)
    {
        if (!string.Equals(record.RequestHash, hash, StringComparison.Ordinal))
        {
            await Problem(context, StatusCodes.Status422UnprocessableEntity, "Idempotency.KeyReused",
                $"This {HeaderName} was already used for a different request.");
            return;
        }

        if (record.CompletedAt is null)
        {
            await Problem(context, StatusCodes.Status409Conflict, "Idempotency.InProgress",
                $"A request with this {HeaderName} is still being processed.");
            return;
        }

        context.Response.StatusCode = record.StatusCode ?? StatusCodes.Status200OK;
        context.Response.Headers[ReplayedHeader] = "true";
        if (record.Location is not null)
        {
            context.Response.Headers.Location = record.Location;
        }

        if (record.ContentType is not null)
        {
            context.Response.ContentType = record.ContentType;
        }

        if (record.ResponseBody is { Length: > 0 } body)
        {
            await context.Response.Body.WriteAsync(body, context.RequestAborted);
        }
    }

    private static async Task<string> HashRequestAsync(HttpRequest request)
    {
        request.EnableBuffering();
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(System.Text.Encoding.UTF8.GetBytes($"{request.Method} {request.Path}{request.QueryString}\n"));
        var chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, request.HttpContext.RequestAborted)) > 0)
        {
            sha.AppendData(chunk, 0, read);
        }

        request.Body.Position = 0;
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static async Task ForgetAsync(PlatformDbContext db, IdempotencyRecord record)
    {
        db.IdempotencyRecords.Remove(record);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static Task Problem(HttpContext context, int status, string title, string detail) =>
        Results.Problem(statusCode: status, title: title, detail: detail).ExecuteAsync(context);
}
