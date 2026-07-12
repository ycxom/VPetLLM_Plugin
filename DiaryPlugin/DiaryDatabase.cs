using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace DiaryPlugin
{
    /// <summary>
    /// 一篇日记。Date 为本地日期 yyyy-MM-dd，作为唯一键。
    /// Vector 为 L2 归一化向量（可空，向量化未启用时为 null）。
    /// </summary>
    public class DiaryEntry
    {
        public string Date { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public float[]? Vector { get; set; }
    }

    /// <summary>
    /// 日记的 SQLite 存储，位于插件的 PluginDataDir/diary.db。
    /// 向量以 BLOB 存储；检索时向量优先（点积=余弦，因入库已归一化），无向量则回退关键词。
    /// </summary>
    public sealed class DiaryDatabase
    {
        private readonly string _connectionString;
        private readonly object _lock = new object();

        public DiaryDatabase(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
            Initialize();
        }

        private void Initialize()
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS diary (
                        id         INTEGER PRIMARY KEY,
                        date       TEXT NOT NULL UNIQUE,
                        content    TEXT NOT NULL,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        dim        INTEGER DEFAULT 0,
                        vector     BLOB
                    );
                    CREATE INDEX IF NOT EXISTS idx_diary_date ON diary(date);
                ";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>写入或覆盖某一天的日记（按 date 唯一）。</summary>
        public void Upsert(string date, string content, float[]? vector)
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO diary (date, content, created_at, dim, vector)
                        VALUES (@date, @content, CURRENT_TIMESTAMP, @dim, @vector)
                        ON CONFLICT(date) DO UPDATE SET
                            content = excluded.content,
                            created_at = excluded.created_at,
                            dim = excluded.dim,
                            vector = excluded.vector;
                    ";
                    cmd.Parameters.AddWithValue("@date", date);
                    cmd.Parameters.AddWithValue("@content", content ?? "");
                    cmd.Parameters.AddWithValue("@dim", vector?.Length ?? 0);
                    cmd.Parameters.AddWithValue("@vector", (object?)VectorToBlob(vector) ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    VPetLLM.Utils.System.Logger.Log($"DiaryDatabase.Upsert 失败 ({date}): {ex.Message}");
                }
            }
        }

        public bool HasEntry(string date)
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT 1 FROM diary WHERE date = @date LIMIT 1";
                    cmd.Parameters.AddWithValue("@date", date);
                    return cmd.ExecuteScalar() != null;
                }
                catch { return false; }
            }
        }

        public DiaryEntry? GetByDate(string date)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT date, content, created_at, dim, vector FROM diary WHERE date = @date";
                cmd.Parameters.AddWithValue("@date", date);
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? ReadEntry(reader) : null;
            }
        }

        /// <summary>取全部日记，按日期倒序（最新在前）。</summary>
        public List<DiaryEntry> GetAll()
        {
            lock (_lock)
            {
                var list = new List<DiaryEntry>();
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT date, content, created_at, dim, vector FROM diary ORDER BY date DESC";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                        list.Add(ReadEntry(reader));
                }
                catch (Exception ex)
                {
                    VPetLLM.Utils.System.Logger.Log($"DiaryDatabase.GetAll 失败: {ex.Message}");
                }
                return list;
            }
        }

        public int Count()
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM diary";
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
                catch { return 0; }
            }
        }

        /// <summary>取所有缺向量（dim=0 或 vector 为空）的日记，用于 embedding 可用后补齐。</summary>
        public List<DiaryEntry> GetEntriesMissingVector()
        {
            lock (_lock)
            {
                var list = new List<DiaryEntry>();
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT date, content, created_at, dim, vector FROM diary WHERE dim = 0 OR vector IS NULL ORDER BY date DESC";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                        list.Add(ReadEntry(reader));
                }
                catch (Exception ex)
                {
                    VPetLLM.Utils.System.Logger.Log($"DiaryDatabase.GetEntriesMissingVector 失败: {ex.Message}");
                }
                return list;
            }
        }

        /// <summary>只更新某天日记的向量，不动正文/时间。</summary>
        public void UpdateVector(string date, float[] vector)
        {
            if (vector is null || vector.Length == 0) return;
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "UPDATE diary SET dim = @dim, vector = @vector WHERE date = @date";
                    cmd.Parameters.AddWithValue("@dim", vector.Length);
                    cmd.Parameters.AddWithValue("@vector", (object?)VectorToBlob(vector) ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@date", date);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    VPetLLM.Utils.System.Logger.Log($"DiaryDatabase.UpdateVector 失败 ({date}): {ex.Message}");
                }
            }
        }

        public void ClearAll()
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM diary";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>删除某一天的日记。</summary>
        public void Delete(string date)
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM diary WHERE date = @date";
                    cmd.Parameters.AddWithValue("@date", date);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    VPetLLM.Utils.System.Logger.Log($"DiaryDatabase.Delete 失败 ({date}): {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 检索日记。queryVector 非空且库中有向量时走余弦（点积）排序，
        /// 否则回退到对内容/日期的关键词包含匹配。返回前 topN 篇。
        /// </summary>
        public List<DiaryEntry> Search(float[]? queryVector, string queryText, int topN)
        {
            var all = GetAll();
            if (all.Count == 0) return all;

            if (queryVector is not null && all.Any(e => e.Vector is not null && e.Vector.Length == queryVector.Length))
            {
                return all
                    .Where(e => e.Vector is not null && e.Vector.Length == queryVector.Length)
                    .Select(e => (entry: e, score: Dot(queryVector, e.Vector!)))
                    .OrderByDescending(x => x.score)
                    .Take(topN)
                    .Select(x => x.entry)
                    .ToList();
            }

            // 关键词回退：按命中词数简单排序
            var terms = (queryText ?? "").Split(new[] { ' ', '，', ',', '、' }, StringSplitOptions.RemoveEmptyEntries);
            return all
                .Select(e => (entry: e, score: terms.Count(t => e.Content.Contains(t, StringComparison.OrdinalIgnoreCase))))
                .Where(x => string.IsNullOrEmpty(queryText) || x.score > 0)
                .OrderByDescending(x => x.score)
                .ThenByDescending(x => x.entry.Date)
                .Take(topN)
                .Select(x => x.entry)
                .ToList();
        }

        private static DiaryEntry ReadEntry(SqliteDataReader reader)
        {
            var dim = reader.GetInt32(3);
            float[]? vec = null;
            if (!reader.IsDBNull(4) && dim > 0)
            {
                var blob = (byte[])reader[4];
                if (blob.Length == dim * sizeof(float))
                {
                    vec = new float[dim];
                    Buffer.BlockCopy(blob, 0, vec, 0, blob.Length);
                }
            }
            return new DiaryEntry
            {
                Date = reader.GetString(0),
                Content = reader.GetString(1),
                CreatedAt = reader.IsDBNull(2) ? DateTime.Now : reader.GetDateTime(2),
                Vector = vec
            };
        }

        private static byte[]? VectorToBlob(float[]? vector)
        {
            if (vector is null || vector.Length == 0) return null;
            var blob = new byte[vector.Length * sizeof(float)];
            Buffer.BlockCopy(vector, 0, blob, 0, blob.Length);
            return blob;
        }

        private static float Dot(float[] a, float[] b)
        {
            float sum = 0f;
            for (int i = 0; i < a.Length; i++) sum += a[i] * b[i];
            return sum;
        }
    }
}
