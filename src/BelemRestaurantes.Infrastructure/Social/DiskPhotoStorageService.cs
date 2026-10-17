using System.Security.Cryptography;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace BelemRestaurantes.Infrastructure.Social;

public sealed class DiskPhotoStorageService : IPhotoStorageService
{
    private readonly MySqlConnectionFactory _factory;
    private readonly string _baseStoragePath;
    private readonly ILogger<DiskPhotoStorageService> _logger;

    public DiskPhotoStorageService(MySqlConnectionFactory factory, IConfiguration configuration, ILogger<DiskPhotoStorageService> logger)
    {
        _factory = factory;
        _logger = logger;
        var configuredPath = configuration["PhotoStorage:BasePath"];
        _baseStoragePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "App_Data", "Uploads", "Experiences")
            : configuredPath;

        Directory.CreateDirectory(_baseStoragePath);
    }

    public async Task<ExperiencePhoto> ProcessAndSaveAsync(
        Stream inputStream,
        string originalFileName,
        string contentType,
        string userId,
        long restaurantId,
        long experienceId,
        string? caption,
        CancellationToken ct)
    {
        if (inputStream.Length is <= 0 or > PhotoRules.MaxFileSizeBytes)
        {
            throw new ArgumentException("Tamanho de arquivo inválido. Limite de até 8MB.");
        }

        using var memory = new MemoryStream();
        await inputStream.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();

        if (!PhotoRules.IsSupportedMagicBytes(bytes.AsSpan(), out _))
        {
            throw new ArgumentException("Formato de imagem não suportado. Envie JPEG, PNG ou WebP legítimo.");
        }

        using var originalBitmap = SKBitmap.Decode(bytes)
            ?? throw new ArgumentException("Não foi possível decodificar os dados da imagem.");

        var idPart = Guid.NewGuid().ToString("N");
        var fileName = $"{idPart}.webp";
        var thumbName = $"{idPart}_thumb.webp";

        var (resizedWidth, resizedHeight) = CalculateProportionalDimensions(originalBitmap.Width, originalBitmap.Height, PhotoRules.MaxDimensionPixels);
        using var resizedBitmap = originalBitmap.Resize(new SKImageInfo(resizedWidth, resizedHeight), SKSamplingOptions.Default);
        using var resizedImage = SKImage.FromBitmap(resizedBitmap);
        using var encodedFull = resizedImage.Encode(SKEncodedImageFormat.Webp, 82);

        var (thumbWidth, thumbHeight) = CalculateProportionalDimensions(originalBitmap.Width, originalBitmap.Height, PhotoRules.ThumbnailDimensionPixels);
        using var thumbBitmap = originalBitmap.Resize(new SKImageInfo(thumbWidth, thumbHeight), SKSamplingOptions.Default);
        using var thumbImage = SKImage.FromBitmap(thumbBitmap);
        using var encodedThumb = thumbImage.Encode(SKEncodedImageFormat.Webp, 75);

        var fullPath = Path.Combine(_baseStoragePath, fileName);
        var thumbPath = Path.Combine(_baseStoragePath, thumbName);

        await using (var fileStream = File.Create(fullPath))
        {
            encodedFull.SaveTo(fileStream);
        }

        await using (var fileStream = File.Create(thumbPath))
        {
            encodedThumb.SaveTo(fileStream);
        }

        var photo = new ExperiencePhoto
        {
            ExperienceId = experienceId,
            RestaurantId = restaurantId,
            UserId = userId,
            FileName = fileName,
            ThumbnailName = thumbName,
            OriginalName = Path.GetFileName(originalFileName),
            ContentType = "image/webp",
            FileSize = (int)encodedFull.Size,
            Width = resizedWidth,
            Height = resizedHeight,
            Caption = caption?.Trim(),
            PhotoType = "community",
            ModerationStatus = "pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var connection = await _factory.OpenAsync(ct);
        const string insertSql = """
            INSERT INTO experience_photos (
                experience_id, restaurant_id, user_id, file_name, thumbnail_name,
                original_name, content_type, file_size, width, height,
                caption, photo_type, moderation_status, created_at
            ) VALUES (
                @ExperienceId, @RestaurantId, @UserId, @FileName, @ThumbnailName,
                @OriginalName, @ContentType, @FileSize, @Width, @Height,
                @Caption, @PhotoType, @ModerationStatus, @CreatedAt
            );
            SELECT LAST_INSERT_ID();
            """;

        photo.Id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(insertSql, photo, cancellationToken: ct));
        _logger.LogInformation("Foto {Id} salva e sanitizada com sucesso ({FileName})", photo.Id, photo.FileName);

        return photo;
    }

    public async Task<(Stream Stream, string ContentType, string FileName)?> GetPhotoFileAsync(
        string fileName,
        string? viewerUserId,
        bool isThumb,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
        {
            return null;
        }

        await using var connection = await _factory.OpenAsync(ct);
        var query = """
            SELECT p.id, p.file_name, p.thumbnail_name, p.user_id, p.moderation_status,
                   e.visibility, e.status AS experience_status
            FROM experience_photos p
            JOIN user_experiences e ON e.id = p.experience_id
            WHERE p.file_name = @fileName OR p.thumbnail_name = @fileName
            LIMIT 1
            """;

        var meta = await connection.QuerySingleOrDefaultAsync<PhotoMetaRow>(new CommandDefinition(query, new { fileName }, cancellationToken: ct));
        if (meta is null) return null;

        // Regra de privacidade:
        // Pública aprovada -> qualquer um pode ver.
        // Privada -> apenas o próprio usuário dono.
        // Pendente de moderação -> apenas o dono pode ver antes de aprovar.
        var isOwner = !string.IsNullOrEmpty(viewerUserId) && meta.user_id == viewerUserId;
        var isPublicApproved = meta.moderation_status == "approved" && meta.experience_status == "approved" && meta.visibility == "public";
        var isUnlisted = meta.visibility == "unlisted";

        if (!isPublicApproved && !isOwner && !isUnlisted)
        {
            return null; // Acesso negado
        }

        var targetFile = isThumb ? meta.thumbnail_name : meta.file_name;
        var fullPath = Path.Combine(_baseStoragePath, targetFile);

        if (!File.Exists(fullPath))
        {
            return null;
        }

        var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return (fileStream, "image/webp", targetFile);
    }

    public async Task<RestaurantGallery> GetGalleryForRestaurantAsync(long restaurantId, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        const string sql = """
            SELECT p.id, p.experience_id AS ExperienceId, p.restaurant_id AS RestaurantId,
                   p.file_name AS FileName, p.thumbnail_name AS ThumbnailName, p.caption AS Caption,
                   p.photo_type AS PhotoType, p.moderation_status AS ModerationStatus,
                   p.width AS Width, p.height AS Height
            FROM experience_photos p
            JOIN user_experiences e ON e.id = p.experience_id
            WHERE p.restaurant_id = @restaurantId
              AND p.moderation_status = 'approved'
              AND e.status = 'approved'
              AND e.visibility = 'public'
            ORDER BY p.created_at DESC
            LIMIT 60
            """;

        var photos = (await connection.QueryAsync<ExperiencePhotoView>(new CommandDefinition(sql, new { restaurantId }, cancellationToken: ct))).AsList();

        var official = photos.Where(p => p.PhotoType == "official").ToList();
        var community = photos.Where(p => p.PhotoType == "community").ToList();
        var illustrative = photos.Where(p => p.PhotoType == "illustrative").ToList();

        return new RestaurantGallery(official, community, illustrative);
    }

    public async Task<IReadOnlyList<ExperiencePhoto>> ListPendingPhotosAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        await using var connection = await _factory.OpenAsync(ct);
        const string sql = """
            SELECT id, experience_id AS ExperienceId, restaurant_id AS RestaurantId, user_id AS UserId,
                   file_name AS FileName, thumbnail_name AS ThumbnailName, original_name AS OriginalName,
                   content_type AS ContentType, file_size AS FileSize, width AS Width, height AS Height,
                   caption AS Caption, photo_type AS PhotoType, moderation_status AS ModerationStatus,
                   created_at AS CreatedAt
            FROM experience_photos
            WHERE moderation_status = 'pending'
            ORDER BY created_at ASC
            LIMIT @limit OFFSET @offset
            """;

        var rows = await connection.QueryAsync<ExperiencePhoto>(new CommandDefinition(sql, new { limit = pageSize, offset }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> ModeratePhotoAsync(long photoId, string newStatus, string moderatorActor, CancellationToken ct)
    {
        if (newStatus is not ("approved" or "rejected")) return false;

        await using var connection = await _factory.OpenAsync(ct);
        const string sql = """
            UPDATE experience_photos
            SET moderation_status = @newStatus, moderated_by = @moderatorActor, moderated_at = UTC_TIMESTAMP(6)
            WHERE id = @photoId
            """;

        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { photoId, newStatus, moderatorActor }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> DeletePhotoAsync(long photoId, string userId, bool isAdmin, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var photo = await connection.QuerySingleOrDefaultAsync<PhotoMetaRow>(new CommandDefinition(
            "SELECT id, file_name, thumbnail_name, user_id FROM experience_photos WHERE id = @photoId",
            new { photoId }, cancellationToken: ct));

        if (photo is null) return false;
        if (!isAdmin && photo.user_id != userId) return false;

        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM experience_photos WHERE id = @photoId", new { photoId }, cancellationToken: ct));

        try
        {
            var p1 = Path.Combine(_baseStoragePath, photo.file_name);
            var p2 = Path.Combine(_baseStoragePath, photo.thumbnail_name);
            if (File.Exists(p1)) File.Delete(p1);
            if (File.Exists(p2)) File.Delete(p2);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao apagar arquivo de foto do disco para photoId {Id}", photoId);
        }

        return true;
    }

    private static (int Width, int Height) CalculateProportionalDimensions(int width, int height, int maxDimension)
    {
        if (width <= maxDimension && height <= maxDimension)
        {
            return (width, height);
        }

        if (width >= height)
        {
            var h = (int)Math.Round((double)height * maxDimension / width);
            return (maxDimension, Math.Max(1, h));
        }
        else
        {
            var w = (int)Math.Round((double)width * maxDimension / height);
            return (Math.Max(1, w), maxDimension);
        }
    }

    private sealed class PhotoMetaRow
    {
        public long id { get; set; }
        public string file_name { get; set; } = "";
        public string thumbnail_name { get; set; } = "";
        public string user_id { get; set; } = "";
        public string moderation_status { get; set; } = "";
        public string visibility { get; set; } = "";
        public string experience_status { get; set; } = "";
    }
}
