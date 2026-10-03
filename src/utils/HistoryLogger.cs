using System.IO;
using System.Text;
using System.Globalization;
using Microsoft.Data.Sqlite;
using CsvHelper;

using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    public static class SQLiteHistoryLogger
    {
        public static readonly string CONNECTION_STRING = "Data Source=translation_history.db;";

        private static SqliteConnection _sharedConnection;
        private static readonly object _connectionLock = new object();

        static SQLiteHistoryLogger()
        {
            InitializeDatabase();
        }

        private static void InitializeDatabase()
        {
            GetConnection();

            using (var command = new SqliteCommand(@"
                CREATE TABLE IF NOT EXISTS TranslationHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT,
                    SourceText TEXT,
                    TranslatedText TEXT,
                    TargetLanguage TEXT,
                    ApiUsed TEXT
                );", GetConnection()))
            {
                command.ExecuteNonQuery();
            }
        }

        private static SqliteConnection GetConnection()
        {
            lock (_connectionLock)
            {
                if (_sharedConnection == null)
                {
                    _sharedConnection = new SqliteConnection(CONNECTION_STRING);
                    _sharedConnection.Open();
                }
                else if (_sharedConnection.State != System.Data.ConnectionState.Open)
                {
                    try
                    {
                        _sharedConnection.Open();
                    }
                    catch
                    {
                        _sharedConnection.Dispose();
                        _sharedConnection = new SqliteConnection(CONNECTION_STRING);
                        _sharedConnection.Open();
                    }
                }

                return _sharedConnection;
            }
        }

        public static async Task LogTranslation(string sourceText, string translatedText,
            string targetLanguage, string apiUsed, CancellationToken token = default)
        {
            string insertQuery = @"
                INSERT INTO TranslationHistory (Timestamp, SourceText, TranslatedText, TargetLanguage, ApiUsed)
                VALUES (@Timestamp, @SourceText, @TranslatedText, @TargetLanguage, @ApiUsed)";

            using (var command = new SqliteCommand(insertQuery, GetConnection()))
            {
                command.Parameters.AddWithValue("@Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                command.Parameters.AddWithValue("@SourceText", sourceText);
                command.Parameters.AddWithValue("@TranslatedText", translatedText);
                command.Parameters.AddWithValue("@TargetLanguage", targetLanguage);
                command.Parameters.AddWithValue("@ApiUsed", apiUsed);
                await command.ExecuteNonQueryAsync(token);
            }
        }

        public static async Task<(List<TranslationHistoryEntry>, int)> LoadHistoryAsync(
            int page, int maxRow, string searchText, CancellationToken token = default)
        {
            var history = new List<TranslationHistoryEntry>();
            int totalCount = 0;
            using (var command = new SqliteCommand(@"
                SELECT COUNT(*) 
                FROM TranslationHistory
                WHERE SourceText LIKE @search OR TranslatedText LIKE @search", GetConnection()))

            {
                command.Parameters.AddWithValue("@search", $"%{searchText}%");
                totalCount = Convert.ToInt32(await command.ExecuteScalarAsync(token));
            }

            // 计算最大页数，至少为 1
            int maxPage = Math.Max(1, (int)Math.Ceiling(totalCount / (double)maxRow));
            int offset = Math.Max(0, (page - 1) * maxRow);

            using (var command = new SqliteCommand(@"
                SELECT Timestamp, SourceText, TranslatedText, TargetLanguage, ApiUsed
                FROM TranslationHistory
                WHERE SourceText LIKE @search OR TranslatedText LIKE @search
                ORDER BY Timestamp DESC
                LIMIT @maxRow OFFSET @offset", GetConnection()))

            {
                command.Parameters.AddWithValue("@search", $"%{searchText}%");
                command.Parameters.AddWithValue("@maxRow", maxRow);
                command.Parameters.AddWithValue("@offset", offset);

                using (var reader = await command.ExecuteReaderAsync(token))
                {
                    while (await reader.ReadAsync(token))
                    {
                        string unixTime = reader.GetString(reader.GetOrdinal("Timestamp"));
                        DateTime localTime;
                        try
                        {
                            localTime = DateTimeOffset.FromUnixTimeSeconds((long)Convert.ToDouble(unixTime)).LocalDateTime;
                        }
                        catch (FormatException)
                        {
                            // DEPRECATED
                            await MigrateOldTimestampFormat();
                            return await LoadHistoryAsync(page, maxRow, string.Empty);
                        }
                        history.Add(new TranslationHistoryEntry
                        {
                            Timestamp = localTime.ToString("yyyy-MM-dd HH:mm"),
                            TimestampFull = localTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            SourceText = reader.GetString(reader.GetOrdinal("SourceText")),
                            TranslatedText = reader.GetString(reader.GetOrdinal("TranslatedText")),
                            TargetLanguage = reader.GetString(reader.GetOrdinal("TargetLanguage")),
                            ApiUsed = reader.GetString(reader.GetOrdinal("ApiUsed"))
                        });
                    }
                }
            }
            return (history, maxPage);
        }

        public static async Task ClearHistory(CancellationToken token = default)
        {
            string selectQuery = "DELETE FROM TranslationHistory; DELETE FROM sqlite_sequence WHERE NAME='TranslationHistory'";
            using (var command = new SqliteCommand(selectQuery, GetConnection()))
            {
                await command.ExecuteNonQueryAsync(token);
            }
        }

        public static async Task<string?> LoadLastSourceText(CancellationToken token = default)
        {
            string selectQuery = @"
                SELECT SourceText
                FROM TranslationHistory
                ORDER BY Id DESC
                LIMIT 1";

            using (var command = new SqliteCommand(selectQuery, GetConnection()))
            using (var reader = await command.ExecuteReaderAsync(token))
            {
                if (await reader.ReadAsync(token))
                    return reader.GetString(reader.GetOrdinal("SourceText"));
                else
                    return null;
            }
        }

        public static async Task<TranslationHistoryEntry?> LoadLastTranslation(CancellationToken token = default)
        {
            string selectQuery = @"
                SELECT Timestamp, SourceText, TranslatedText, TargetLanguage, ApiUsed
                FROM TranslationHistory
                ORDER BY Id DESC
                LIMIT 1";

            using (var command = new SqliteCommand(selectQuery, GetConnection()))
            using (var reader = await command.ExecuteReaderAsync(token))
            {
                if (await reader.ReadAsync(token))
                {
                    string unixTime = reader.GetString(reader.GetOrdinal("Timestamp"));
                    DateTime localTime = DateTimeOffset.FromUnixTimeSeconds((long)Convert.ToDouble(unixTime)).LocalDateTime;
                    return new TranslationHistoryEntry
                    {
                        Timestamp = localTime.ToString("yyyy-MM-dd HH:mm"),
                        TimestampFull = localTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        SourceText = reader.GetString(reader.GetOrdinal("SourceText")),
                        TranslatedText = reader.GetString(reader.GetOrdinal("TranslatedText")),
                        TargetLanguage = reader.GetString(reader.GetOrdinal("TargetLanguage")),
                        ApiUsed = reader.GetString(reader.GetOrdinal("ApiUsed"))
                    };
                }
                return null;
            }
        }

        public static async Task DeleteLastTranslation(CancellationToken token = default)
        {
            using (var command = new SqliteCommand(@"
                DELETE FROM TranslationHistory
                WHERE Id IN (SELECT Id FROM TranslationHistory ORDER BY Id DESC LIMIT 1)",
                GetConnection()))
            {
                await command.ExecuteNonQueryAsync(token);
            }
        }

        public static async Task ExportToCSV(string filePath, CancellationToken token = default)
        {
            var history = new List<TranslationHistoryEntry>();

            string selectQuery = @"
                SELECT Timestamp, SourceText, TranslatedText, TargetLanguage, ApiUsed
                FROM TranslationHistory
                ORDER BY Timestamp DESC";

            using (var command = new SqliteCommand(selectQuery, GetConnection()))
            using (var reader = await command.ExecuteReaderAsync(token))
            {
                while (await reader.ReadAsync(token))
                {
                    string unixTime = reader.GetString(reader.GetOrdinal("Timestamp"));
                    DateTime localTime = DateTimeOffset.FromUnixTimeSeconds((long)Convert.ToDouble(unixTime)).LocalDateTime;
                    history.Add(new TranslationHistoryEntry
                    {
                        Timestamp = localTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        TimestampFull = localTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        SourceText = reader.GetString(reader.GetOrdinal("SourceText")),
                        TranslatedText = reader.GetString(reader.GetOrdinal("TranslatedText")),
                        TargetLanguage = reader.GetString(reader.GetOrdinal("TargetLanguage")),
                        ApiUsed = reader.GetString(reader.GetOrdinal("ApiUsed"))
                    });
                }
            }

            using var writer = new StreamWriter(filePath, false, new UTF8Encoding(true));
            using var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture);
            await csvWriter.WriteRecordsAsync(history, token);
        }

        public static async Task ExportToSRT(string filePath, CancellationToken token = default)
        {
            var entries = new List<(DateTime Time, string Text)>();
            using (var command = new SqliteCommand(@"
                SELECT Timestamp, SourceText, TranslatedText
                FROM TranslationHistory
                ORDER BY Timestamp ASC, Id ASC", GetConnection()))
            using (var reader = await command.ExecuteReaderAsync(token))
            {
                while (await reader.ReadAsync(token))
                {
                    try
                    {
                        entries.Add((
                            DateTimeOffset.FromUnixTimeSeconds((long)Convert.ToDouble(
                                reader.GetString(reader.GetOrdinal("Timestamp")))).LocalDateTime,
                            CombineSRTEntry(
                                reader.GetString(reader.GetOrdinal("SourceText")),
                                reader.GetString(reader.GetOrdinal("TranslatedText")))));
                    }
                    catch (FormatException)
                    {
                        // Skip rows written by the deprecated timestamp format.
                    }
                }
            }

            using var writer = new StreamWriter(filePath, false, new UTF8Encoding(true));
            for (int i = 0; i < entries.Count; i++)
            {
                var start = entries[i].Time;
                // Show each entry until the next one appears (capped) so subtitles stay readable.
                var end = i + 1 < entries.Count
                    ? (entries[i + 1].Time > start.AddSeconds(5) ? start.AddSeconds(5) : entries[i + 1].Time)
                    : start.AddSeconds(3);
                if (end <= start)
                    end = start.AddSeconds(1);

                await writer.WriteLineAsync((i + 1).ToString());
                await writer.WriteLineAsync($"{start:HH:mm:ss,fff} --> {end:HH:mm:ss,fff}");
                await writer.WriteLineAsync(entries[i].Text);
                await writer.WriteLineAsync();
            }
        }

        private static string CombineSRTEntry(string sourceText, string translatedText)
        {
            sourceText = sourceText.Replace("\n", " ").Trim();
            if (string.IsNullOrEmpty(translatedText) || translatedText == "N/A")
                return sourceText;
            return $"{sourceText}\n{translatedText.Replace("\n", " ").Trim()}";
        }

        // DEPRECATED
        private static async Task MigrateOldTimestampFormat()
        {
            var records = new List<(long id, string timestamp)>();
            using (var command = new SqliteCommand("SELECT Id, Timestamp FROM TranslationHistory", GetConnection()))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    long id = reader.GetInt64(reader.GetOrdinal("Id"));
                    string timestamp = reader.GetString(reader.GetOrdinal("Timestamp"));
                    records.Add((id, timestamp));
                }
            }

            foreach (var (id, timestamp) in records)
            {
                if (DateTime.TryParse(timestamp, out DateTime dt))
                {
                    long unixTime = ((DateTimeOffset)dt).ToUnixTimeSeconds();
                    using var updateCommand = new SqliteCommand(
                        "UPDATE TranslationHistory SET Timestamp = @Timestamp WHERE Id = @Id",
                        GetConnection());
                    updateCommand.Parameters.AddWithValue("@Id", id);
                    updateCommand.Parameters.AddWithValue("@Timestamp", unixTime.ToString());
                    await updateCommand.ExecuteNonQueryAsync();
                }
            }
        }
    }
}