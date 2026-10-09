using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SlimClip.Core.Models;

namespace SlimClip.Core.Services;

public class ClipboardHistory : IClipboardHistory
{
    private readonly string _connectionString;
    private readonly string _imagesDir;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isDisposed;

    public ClipboardHistory(string dbPath, string imagesDir)
    {
        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        _imagesDir = imagesDir;
        if (!Directory.Exists(_imagesDir))
        {
            Directory.CreateDirectory(_imagesDir);
        }
    }

    public async Task InitializeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Enable WAL mode for high performance concurrent access
            await using (var pragmaCmd = conn.CreateCommand())
            {
                pragmaCmd.CommandText = @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA cache_size = -2000;
                ";
                await pragmaCmd.ExecuteNonQueryAsync();
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS clipboard_history (
                        id           TEXT PRIMARY KEY,
                        text         TEXT NOT NULL,
                        preview      TEXT NOT NULL DEFAULT '',
                        html_content TEXT,
                        rtf_content  TEXT,
                        type         INTEGER NOT NULL DEFAULT 0,
                        copied_at    TEXT NOT NULL,
                        is_favorite  INTEGER NOT NULL DEFAULT 0,
                        image_path   TEXT,
                        image_width  INTEGER NOT NULL DEFAULT 0,
                        image_height INTEGER NOT NULL DEFAULT 0,
                        image_hash   TEXT,
                        file_paths   TEXT
                    );

                    CREATE INDEX IF NOT EXISTS idx_clipboard_sort ON clipboard_history(copied_at DESC);
                    CREATE INDEX IF NOT EXISTS idx_clipboard_favorite ON clipboard_history(is_favorite, copied_at DESC);
                    CREATE INDEX IF NOT EXISTS idx_clipboard_hash ON clipboard_history(image_hash) WHERE image_hash IS NOT NULL;
                ";
                await cmd.ExecuteNonQueryAsync();

                // Ensure is_favorite column exists if upgrading from an existing database
                try
                {
                    await using var alterCmd = conn.CreateCommand();
                    alterCmd.CommandText = "ALTER TABLE clipboard_history ADD COLUMN is_favorite INTEGER NOT NULL DEFAULT 0;";
                    await alterCmd.ExecuteNonQueryAsync();
                }
                catch { }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ClipboardItem> RecordAsync(ClipboardData data)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var now = DateTime.Now;
            var nowString = now.ToString("o", CultureInfo.InvariantCulture);

            if (data.Type == ClipboardType.Image && data.ImageBytes != null && data.ImageBytes.Length > 0)
            {
                var hash = Convert.ToHexString(SHA256.HashData(data.ImageBytes)).ToLowerInvariant();

                // Check if image already exists by hash
                await using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = "SELECT id, image_path, image_width, image_height, is_favorite FROM clipboard_history WHERE image_hash = @hash LIMIT 1";
                checkCmd.Parameters.AddWithValue("@hash", hash);

                await using var reader = await checkCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var existingId = reader.GetString(0);
                    var imgPath = reader.GetString(1);
                    var imgW = reader.GetInt32(2);
                    var imgH = reader.GetInt32(3);
                    var isFavorite = reader.GetInt32(4) == 1;
                    await reader.CloseAsync();
                    await UpdateCopiedAtAsync(conn, existingId, nowString);

                    return new ClipboardItem
                    {
                        Id = existingId,
                        Type = ClipboardType.Image,
                        ImageHash = hash,
                        ImagePath = imgPath,
                        ImageWidth = imgW,
                        ImageHeight = imgH,
                        CopiedAt = now,
                        IsFavorite = isFavorite,
                        Text = "[图片]"
                    };
                }
                await reader.CloseAsync();

                // New image: write to disk
                var fileName = $"{hash}.png";
                var fullPath = Path.Combine(_imagesDir, fileName);
                if (!File.Exists(fullPath))
                {
                    await File.WriteAllBytesAsync(fullPath, data.ImageBytes);
                }

                // Relative path for database
                var relativePath = Path.Combine(Path.GetFileName(_imagesDir), fileName).Replace('\\', '/');
                var newItem = new ClipboardItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = ClipboardType.Image,
                    Text = "[图片]",
                    Preview = "[图片]",
                    ImageHash = hash,
                    ImagePath = relativePath,
                    ImageWidth = data.ImageWidth,
                    ImageHeight = data.ImageHeight,
                    CopiedAt = now
                };

                await using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO clipboard_history (id, text, preview, type, copied_at, is_favorite, image_path, image_width, image_height, image_hash)
                    VALUES (@id, @text, @preview, @type, @copied_at, 0, @image_path, @image_width, @image_height, @image_hash);
                ";
                insertCmd.Parameters.AddWithValue("@id", newItem.Id);
                insertCmd.Parameters.AddWithValue("@text", newItem.Text);
                insertCmd.Parameters.AddWithValue("@preview", newItem.Preview);
                insertCmd.Parameters.AddWithValue("@type", (int)newItem.Type);
                insertCmd.Parameters.AddWithValue("@copied_at", nowString);
                insertCmd.Parameters.AddWithValue("@image_path", newItem.ImagePath);
                insertCmd.Parameters.AddWithValue("@image_width", newItem.ImageWidth);
                insertCmd.Parameters.AddWithValue("@image_height", newItem.ImageHeight);
                insertCmd.Parameters.AddWithValue("@image_hash", newItem.ImageHash);
                await insertCmd.ExecuteNonQueryAsync();

                return newItem;
            }
            else if (data.Type == ClipboardType.File && data.FilePaths != null && data.FilePaths.Length > 0)
            {
                var json = JsonSerializer.Serialize(data.FilePaths);
                var text = string.Join(Environment.NewLine, data.FilePaths);

                await using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = "SELECT id, is_favorite FROM clipboard_history WHERE type = 2 AND file_paths = @json LIMIT 1";
                checkCmd.Parameters.AddWithValue("@json", json);

                await using var reader = await checkCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var existingId = reader.GetString(0);
                    var isFavorite = reader.GetInt32(1) == 1;
                    await reader.CloseAsync();
                    await UpdateCopiedAtAsync(conn, existingId, nowString);

                    return new ClipboardItem
                    {
                        Id = existingId,
                        Type = ClipboardType.File,
                        Text = text,
                        FilePaths = json,
                        CopiedAt = now,
                        IsFavorite = isFavorite
                    };
                }
                await reader.CloseAsync();

                var newItem = new ClipboardItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = ClipboardType.File,
                    Text = text,
                    Preview = text,
                    FilePaths = json,
                    CopiedAt = now
                };

                await using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO clipboard_history (id, text, preview, type, copied_at, is_favorite, file_paths)
                    VALUES (@id, @text, @preview, @type, @copied_at, 0, @file_paths);
                ";
                insertCmd.Parameters.AddWithValue("@id", newItem.Id);
                insertCmd.Parameters.AddWithValue("@text", newItem.Text);
                insertCmd.Parameters.AddWithValue("@preview", newItem.Preview);
                insertCmd.Parameters.AddWithValue("@type", (int)newItem.Type);
                insertCmd.Parameters.AddWithValue("@copied_at", nowString);
                insertCmd.Parameters.AddWithValue("@file_paths", json);
                await insertCmd.ExecuteNonQueryAsync();

                return newItem;
            }
            else
            {
                var text = data.Text ?? string.Empty;
                var preview = ClipboardItem.BuildPreview(text);

                await using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = "SELECT id, is_favorite FROM clipboard_history WHERE type = 0 AND text = @text LIMIT 1";
                checkCmd.Parameters.AddWithValue("@text", text);

                await using var reader = await checkCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var existingId = reader.GetString(0);
                    var isFavorite = reader.GetInt32(1) == 1;
                    await reader.CloseAsync();

                    await using var updateCmd = conn.CreateCommand();
                    updateCmd.CommandText = @"
                        UPDATE clipboard_history 
                        SET copied_at = @now, html_content = @html, rtf_content = @rtf
                        WHERE id = @id;
                    ";
                    updateCmd.Parameters.AddWithValue("@now", nowString);
                    updateCmd.Parameters.AddWithValue("@html", (object?)data.HtmlContent ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@rtf", (object?)data.RtfContent ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@id", existingId);
                    await updateCmd.ExecuteNonQueryAsync();

                    return new ClipboardItem
                    {
                        Id = existingId,
                        Type = ClipboardType.Text,
                        Text = text,
                        Preview = preview,
                        HtmlContent = data.HtmlContent,
                        RtfContent = data.RtfContent,
                        CopiedAt = now,
                        IsFavorite = isFavorite
                    };
                }
                await reader.CloseAsync();

                var newItem = new ClipboardItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = ClipboardType.Text,
                    Text = text,
                    Preview = preview,
                    HtmlContent = data.HtmlContent,
                    RtfContent = data.RtfContent,
                    CopiedAt = now
                };

                await using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO clipboard_history (id, text, preview, html_content, rtf_content, type, copied_at, is_favorite)
                    VALUES (@id, @text, @preview, @html, @rtf, 0, @copied_at, 0);
                ";
                insertCmd.Parameters.AddWithValue("@id", newItem.Id);
                insertCmd.Parameters.AddWithValue("@text", newItem.Text);
                insertCmd.Parameters.AddWithValue("@preview", newItem.Preview);
                insertCmd.Parameters.AddWithValue("@html", (object?)newItem.HtmlContent ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@rtf", (object?)newItem.RtfContent ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@copied_at", nowString);
                await insertCmd.ExecuteNonQueryAsync();

                return newItem;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<ClipboardItem>> GetAllItemsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var list = new List<ClipboardItem>();
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, text, preview, html_content, rtf_content, type, copied_at, is_favorite,
                       image_path, image_width, image_height, image_hash, file_paths
                FROM clipboard_history
                ORDER BY copied_at DESC;
            ";

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var item = new ClipboardItem
                {
                    Id = reader.GetString(0),
                    Text = reader.GetString(1),
                    Preview = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    HtmlContent = reader.IsDBNull(3) ? null : reader.GetString(3),
                    RtfContent = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Type = (ClipboardType)reader.GetInt32(5),
                    CopiedAt = DateTime.TryParse(reader.GetString(6), null, DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.Now,
                    IsFavorite = reader.GetInt32(7) == 1,
                    ImagePath = reader.IsDBNull(8) ? null : reader.GetString(8),
                    ImageWidth = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                    ImageHeight = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                    ImageHash = reader.IsDBNull(11) ? null : reader.GetString(11),
                    FilePaths = reader.IsDBNull(12) ? null : reader.GetString(12)
                };
                list.Add(item);
            }

            return list;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteItemAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            string? imageHash = null;
            await using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT image_hash FROM clipboard_history WHERE id = @id";
                checkCmd.Parameters.AddWithValue("@id", id);
                var result = await checkCmd.ExecuteScalarAsync();
                imageHash = result as string;
            }

            await using (var deleteCmd = conn.CreateCommand())
            {
                deleteCmd.CommandText = "DELETE FROM clipboard_history WHERE id = @id";
                deleteCmd.Parameters.AddWithValue("@id", id);
                var affected = await deleteCmd.ExecuteNonQueryAsync();
                if (affected == 0)
                    return false;
            }

            // If image, check if other rows still reference imageHash
            if (!string.IsNullOrEmpty(imageHash))
            {
                await using var refCmd = conn.CreateCommand();
                refCmd.CommandText = "SELECT COUNT(1) FROM clipboard_history WHERE image_hash = @hash";
                refCmd.Parameters.AddWithValue("@hash", imageHash);
                var count = Convert.ToInt32(await refCmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    var filePath = Path.Combine(_imagesDir, $"{imageHash}.png");
                    if (File.Exists(filePath))
                    {
                        try { File.Delete(filePath); } catch { }
                    }
                }
            }

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> ToggleFavoriteAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE clipboard_history SET is_favorite = 1 - is_favorite WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM clipboard_history;";
            await cmd.ExecuteNonQueryAsync();

            try
            {
                if (Directory.Exists(_imagesDir))
                {
                    foreach (var f in Directory.GetFiles(_imagesDir, "*.png"))
                    {
                        File.Delete(f);
                    }
                }
            }
            catch { }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task TrimExcessAsync(int maxItems)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM clipboard_history
                WHERE is_favorite = 0 AND id NOT IN (
                    SELECT id FROM clipboard_history
                    WHERE is_favorite = 0
                    ORDER BY copied_at DESC
                    LIMIT @maxItems
                );
            ";
            cmd.Parameters.AddWithValue("@maxItems", maxItems);
            await cmd.ExecuteNonQueryAsync();

            await CleanOrphanImagesAsync(conn);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task CleanPeriodicAsync(int days)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM clipboard_history
                WHERE is_favorite = 0 AND datetime(copied_at) < datetime('now', '-' || @days || ' days');
            ";
            cmd.Parameters.AddWithValue("@days", days);
            await cmd.ExecuteNonQueryAsync();

            await CleanOrphanImagesAsync(conn);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateTimestampAsync(string id, DateTime timestamp)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE clipboard_history SET copied_at = @copiedAt WHERE id = @id";
            cmd.Parameters.AddWithValue("@copiedAt", timestamp.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> UpdateItemContentAsync(string id, string text, string? htmlContent, string? rtfContent, DateTime updatedAt)
    {
        await _lock.WaitAsync();
        try
        {
            var preview = ClipboardItem.BuildPreview(text);
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE clipboard_history 
                SET text = @text, 
                    preview = @preview, 
                    html_content = @html, 
                    rtf_content = @rtf, 
                    copied_at = @copiedAt
                WHERE id = @id;
            ";
            cmd.Parameters.AddWithValue("@text", text);
            cmd.Parameters.AddWithValue("@preview", preview);
            cmd.Parameters.AddWithValue("@html", (object?)htmlContent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rtf", (object?)rtfContent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@copiedAt", updatedAt.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@id", id);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task CleanOrphanImagesAsync(SqliteConnection conn)
    {
        try
        {
            var usedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT image_hash FROM clipboard_history WHERE image_hash IS NOT NULL";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    usedHashes.Add(reader.GetString(0));
                }
            }

            if (Directory.Exists(_imagesDir))
            {
                foreach (var file in Directory.GetFiles(_imagesDir, "*.png"))
                {
                    var hash = Path.GetFileNameWithoutExtension(file);
                    if (!usedHashes.Contains(hash))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
        }
        catch { }
    }

    private static async Task UpdateCopiedAtAsync(SqliteConnection conn, string id, string nowString)
    {
        await using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE clipboard_history SET copied_at = @now WHERE id = @id";
        updateCmd.Parameters.AddWithValue("@now", nowString);
        updateCmd.Parameters.AddWithValue("@id", id);
        await updateCmd.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
