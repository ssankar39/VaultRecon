using Apache.Arrow;
using Apache.Arrow.Types;
using BetterFileSys.Models;
using lancedb;
using LanceTable = lancedb.Table;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BetterFileSys.Services
{
    public class LanceDbIndexService : IDisposable
    {
        private const int EmbeddingSize = 384;
        private const string TableName = "files";

        private readonly string _dbPath;
        private readonly string _logPath = Path.Combine(Path.GetTempPath(), "BetterFileSys_Debug.log");
        private Connection? _connection;
        private LanceTable? _table;
        private Schema? _schema;
        private bool _isInitialized;

        public LanceDbIndexService(string dbPath)
        {
            _dbPath = dbPath;
        }

        public bool IsReady => _isInitialized && _table != null;

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
            }
            catch { }
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            Directory.CreateDirectory(_dbPath);

            _connection = new Connection();
            await _connection.Connect(_dbPath);

            _schema = BuildSchema();

            var names = await _connection.TableNames();
            if (!names.Contains(TableName))
            {
                await _connection.CreateEmptyTable(TableName, new CreateTableOptions { Schema = _schema });
            }

            _table = await _connection.OpenTable(TableName);
            await EnsureIndexesAsync();

            _isInitialized = true;
            Log("[LANCE] LanceDB initialized");
        }

        public async Task OptimizeAsync()
        {
            if (_table == null)
                return;

            await _table.Optimize(cleanupOlderThan: TimeSpan.FromDays(7));
        }

        public async Task<Dictionary<string, (long ModifiedTicksUtc, long Size)>> GetAllMetadataAsync()
        {
            var metadata = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);

            if (_table == null)
                return metadata;

            var rows = await _table.Query()
                .Select(new[] { "file_path", "modified_ticks", "file_size" })
                .ToList();

            foreach (var row in rows)
            {
                if (!row.TryGetValue("file_path", out var pathObj) || pathObj is not string path)
                    continue;

                long modified = GetLongValue(row, "modified_ticks");
                long size = GetLongValue(row, "file_size");

                metadata[path] = (modified, size);
            }

            return metadata;
        }

        public async Task UpsertBatchAsync(IReadOnlyList<FileIndexRecord> records)
        {
            if (records.Count == 0 || _table == null || _schema == null)
                return;

            var batch = BuildRecordBatch(records, _schema);

            await _table.MergeInsert("file_path")
                .WhenMatchedUpdateAll()
                .WhenNotMatchedInsertAll()
                .Execute(batch);
        }

        public async Task DeleteByPathsAsync(IReadOnlyList<string> filePaths)
        {
            if (_table == null)
                return;

            foreach (var path in filePaths)
            {
                string filter = $"file_path = '{EscapeSql(path)}'";
                await _table.Delete(filter);
            }
        }

        public async Task<List<SearchResult>> SearchByVectorAsync(float[] queryVector, int limit)
        {
            var results = new List<SearchResult>();

            if (_table == null || queryVector.Length != EmbeddingSize)
                return results;

            var query = queryVector.Select(v => (double)v).ToArray();

            var rows = await _table.Query()
                .NearestTo(query)
                .DistanceType(DistanceType.Cosine)
                .Limit(limit)
                .ToList();

            foreach (var row in rows)
            {
                string filePath = GetStringValue(row, "file_path");
                if (string.IsNullOrWhiteSpace(filePath))
                    continue;

                var result = new SearchResult
                {
                    FilePath = filePath,
                    FileName = GetStringValue(row, "file_name"),
                    FileType = GetStringValue(row, "file_type"),
                    FileSize = GetLongValue(row, "file_size"),
                    Modified = DateTime.FromFileTimeUtc(GetLongValue(row, "modified_ticks")),
                    SearchType = SearchType.Semantic,
                    RelevanceScore = ScoreFromDistance(GetDoubleValue(row, "_distance"))
                };

                results.Add(result);
            }

            return results;
        }

        private async Task EnsureIndexesAsync()
        {
            if (_table == null)
                return;

            try
            {
                await _table.CreateIndex(new[] { "vector" }, new HnswSqIndex
                {
                    DistanceType = DistanceType.Cosine
                });
            }
            catch (Exception ex)
            {
                Log($"[LANCE] Index creation skipped: {ex.Message}");
            }
        }

        private static Schema BuildSchema()
        {
            var vectorField = new Field("item", FloatType.Default, nullable: false);
            var vectorType = new FixedSizeListType(vectorField, EmbeddingSize);

            return new Schema.Builder()
                .Field(new Field("file_path", StringType.Default, nullable: false))
                .Field(new Field("file_name", StringType.Default, nullable: false))
                .Field(new Field("file_type", StringType.Default, nullable: false))
                .Field(new Field("file_size", Int64Type.Default, nullable: false))
                .Field(new Field("modified_ticks", Int64Type.Default, nullable: false))
                .Field(new Field("vector", vectorType, nullable: false))
                .Build();
        }

        private static RecordBatch BuildRecordBatch(IReadOnlyList<FileIndexRecord> records, Schema schema)
        {
            var pathBuilder = new StringArray.Builder();
            var nameBuilder = new StringArray.Builder();
            var typeBuilder = new StringArray.Builder();
            var sizeBuilder = new Int64Array.Builder();
            var modifiedBuilder = new Int64Array.Builder();

            var vectorBuilder = new FixedSizeListArray.Builder(FloatType.Default, EmbeddingSize);
            var vectorValueBuilder = (FloatArray.Builder)vectorBuilder.ValueBuilder;

            foreach (var record in records)
            {
                pathBuilder.Append(record.FilePath);
                nameBuilder.Append(record.FileName);
                typeBuilder.Append(record.FileType);
                sizeBuilder.Append(record.FileSize);
                modifiedBuilder.Append(record.ModifiedTicksUtc);

                vectorBuilder.Append();
                foreach (var value in record.Embedding)
                {
                    vectorValueBuilder.Append(value);
                }
            }

            var arrays = new IArrowArray[]
            {
                pathBuilder.Build(),
                nameBuilder.Build(),
                typeBuilder.Build(),
                sizeBuilder.Build(),
                modifiedBuilder.Build(),
                vectorBuilder.Build()
            };

            return new RecordBatch(schema, arrays, records.Count);
        }

        private static string EscapeSql(string value)
        {
            return value.Replace("'", "''");
        }

        private static long GetLongValue(Dictionary<string, object?> row, string key)
        {
            if (!row.TryGetValue(key, out var value) || value == null)
                return 0;

            return value switch
            {
                long l => l,
                int i => i,
                ulong ul => (long)ul,
                string s when long.TryParse(s, out var parsed) => parsed,
                _ => 0
            };
        }

        private static double GetDoubleValue(Dictionary<string, object?> row, string key)
        {
            if (!row.TryGetValue(key, out var value) || value == null)
                return 0.0;

            return value switch
            {
                double d => d,
                float f => f,
                string s when double.TryParse(s, out var parsed) => parsed,
                _ => 0.0
            };
        }

        private static string GetStringValue(Dictionary<string, object?> row, string key)
        {
            if (!row.TryGetValue(key, out var value) || value == null)
                return string.Empty;

            return value.ToString() ?? string.Empty;
        }

        private static double ScoreFromDistance(double distance)
        {
            if (distance <= 0)
                return 1.0;

            if (distance >= 1.0)
                return 0.0;

            return 1.0 - distance;
        }

        public void Dispose()
        {
            _table?.Dispose();
            _connection?.Dispose();
            _isInitialized = false;
        }
    }
}
