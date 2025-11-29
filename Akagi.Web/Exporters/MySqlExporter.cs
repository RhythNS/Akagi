using Akagi.Web.Data;
using Akagi.Web.Models;
using Akagi.Web.Models.TimeTrackers;
using Dapper;
using Microsoft.Extensions.Options;
using MySqlConnector;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Akagi.Web.Exporters;

public interface IMySqlExporter
{
    public Task ExportAsync(CancellationToken cancellationToken = default);
}

public class MySqlExporterOptions
{
    public required string ConnectionString { get; set; }
}

public class MySqlExporter : IMySqlExporter
{
    private readonly IDefinitionDatabase _definitionDatabase;
    private readonly IEntryDatabase _entryDatabase;
    private readonly IUserDatabase _userDatabase;
    private readonly ILogger<MySqlExporter> _logger;

    private Dictionary<string, string> _userMappings = [];
    private string _connectionString = string.Empty;

    public MySqlExporter(
        IDefinitionDatabase definitionDatabase,
        IEntryDatabase entryDatabase,
        IUserDatabase userDatabase,
        IOptionsMonitor<MySqlExporterOptions> options,
        ILogger<MySqlExporter> logger)
    {
        _definitionDatabase = definitionDatabase;
        _entryDatabase = entryDatabase;
        _userDatabase = userDatabase;
        options.OnChange(OnOptionsChange);
        OnOptionsChange(options.CurrentValue);
        _logger = logger;
    }

    private void OnOptionsChange(MySqlExporterOptions options)
    {
        _connectionString = options.ConnectionString;
    }

    public async Task ExportAsync(CancellationToken cancellationToken = default)
    {
        using MySqlConnection connection = new(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open MySQL connection.");
            throw;
        }

        using MySqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Load everything up-front to avoid cross round-trips later.
            List<Definition> definitions = await _definitionDatabase.GetDocumentsAsync();
            List<Entry> allEntries = await _entryDatabase.GetDocumentsAsync();
            List<User> allUsers = await _userDatabase.GetDocumentsAsync();

            _userMappings = allUsers.ToDictionary(u => u.Id!, u => u.Name ?? u.Id!);

            // Build a lookup of entries by definition to avoid repeated scans.
            Dictionary<string, List<Entry>> entriesByDef = allEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.DefinitionId))
                .GroupBy(e => e.DefinitionId!)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (Definition def in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(def.Id))
                {
                    _logger.LogWarning("Skipping Definition with empty Id.");
                    continue;
                }

                string tableName = BuildTableName(def);
                await EnsurePerDefinitionTableAsync(connection, transaction, tableName, cancellationToken);

                if (!entriesByDef.TryGetValue(def.Id!, out List<Entry>? entries) || entries.Count == 0)
                {
                    continue;
                }

                // Ensure columns exist (add any missing columns before inserts)
                Dictionary<string, (string DataType, string ColumnType)> existingColumns = await GetExistingColumnsAsync(connection, transaction, tableName, cancellationToken);
                await EnsureDefinitionColumnsAsync(connection, transaction, tableName, def, existingColumns, cancellationToken);

                foreach (Entry? entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(entry.Id))
                    {
                        _logger.LogWarning("Skipping Entry with empty Id.");
                        continue;
                    }

                    // Build dynamic insert/upsert per entry based on the definition fields
                    List<string> columnNames = ["EntryId", "CreatedAt"];
                    List<string> columnParams = ["@EntryId", "@CreatedAt"];
                    List<string> updateAssignments = ["`CreatedAt`=VALUES(`CreatedAt`)"];
                    DynamicParameters parameters = new();
                    parameters.Add("EntryId", entry.Id);
                    parameters.Add("CreatedAt", entry.CreatedAt);

                    foreach (FieldDefinition field in def.Fields)
                    {
                        string originalName = field.Name ?? string.Empty;
                        string colName = SanitizeIdentifier(originalName);
                        string paramName = "p_" + colName;

                        columnNames.Add(colName);
                        columnParams.Add("@" + paramName);
                        updateAssignments.Add($"`{colName}`=VALUES(`{colName}`)");

                        parameters.Add(paramName, ConvertValue(field.Type, entry.Values, originalName));
                    }

                    string sql = $@"
INSERT INTO `{tableName}` ({string.Join(", ", columnNames.Select(c => $"`{c}`"))})
VALUES ({string.Join(", ", columnParams)})
ON DUPLICATE KEY UPDATE {string.Join(", ", updateAssignments)};";

                    await connection.ExecuteAsync(
                        new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
                }
            }

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Export completed: Definitions={DefinitionCount}, Entries={EntryCount}", definitions.Count, allEntries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export to MySQL failed. Rolling back.");
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // Creates the per-definition table with base columns if it doesn't exist,
    // and ensures base columns exist if table was pre-existing.
    private static async Task EnsurePerDefinitionTableAsync(
        MySqlConnection conn,
        MySqlTransaction tx,
        string tableName,
        CancellationToken ct)
    {
        string createSql = $@"
CREATE TABLE IF NOT EXISTS `{tableName}` (
    `EntryId` VARCHAR(24) NOT NULL,
    `CreatedAt` DATETIME(6) NOT NULL,
    PRIMARY KEY (`EntryId`)
) ENGINE=InnoDB;";

        await conn.ExecuteAsync(new CommandDefinition(createSql, transaction: tx, cancellationToken: ct));

        // Ensure the base columns exist (for the case table existed with older schema)
        Dictionary<string, (string DataType, string ColumnType)> existing = await GetExistingColumnsAsync(conn, tx, tableName, ct);

        if (!existing.ContainsKey("EntryId"))
        {
            await conn.ExecuteAsync(
                new CommandDefinition($@"ALTER TABLE `{tableName}` ADD COLUMN `EntryId` VARCHAR(24) NOT NULL;", transaction: tx, cancellationToken: ct));
            // Set PK if missing (best-effort)
            await EnsurePrimaryKeyAsync(conn, tx, tableName, "EntryId", ct);
        }

        if (!existing.ContainsKey("CreatedAt"))
        {
            await conn.ExecuteAsync(
                new CommandDefinition($@"ALTER TABLE `{tableName}` ADD COLUMN `CreatedAt` DATETIME(6) NOT NULL;", transaction: tx, cancellationToken: ct));
        }
    }

    private static async Task EnsurePrimaryKeyAsync(MySqlConnection conn, MySqlTransaction tx, string tableName, string pkColumn, CancellationToken ct)
    {
        const string checkPkSql = @"
SELECT CONSTRAINT_NAME
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @TableName AND CONSTRAINT_TYPE = 'PRIMARY KEY';";

        string? constraint = await conn.ExecuteScalarAsync<string?>(
            new CommandDefinition(checkPkSql, new { TableName = tableName }, tx, cancellationToken: ct));

        if (string.IsNullOrEmpty(constraint))
        {
            await conn.ExecuteAsync(
                new CommandDefinition($@"ALTER TABLE `{tableName}` ADD PRIMARY KEY(`{pkColumn}`);", transaction: tx, cancellationToken: ct));
        }
    }

    private static async Task<Dictionary<string, (string DataType, string ColumnType)>> GetExistingColumnsAsync(
        MySqlConnection conn, MySqlTransaction tx, string tableName, CancellationToken ct)
    {
        const string sql = @"
SELECT COLUMN_NAME, DATA_TYPE, COLUMN_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @TableName;";

        IEnumerable<(string COLUMN_NAME, string DATA_TYPE, string COLUMN_TYPE)> rows = await conn.QueryAsync<(string COLUMN_NAME, string DATA_TYPE, string COLUMN_TYPE)>(
            new CommandDefinition(sql, new { TableName = tableName }, tx, cancellationToken: ct));

        return rows.ToDictionary(
            r => r.COLUMN_NAME,
            r => (r.DATA_TYPE, r.COLUMN_TYPE),
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task EnsureDefinitionColumnsAsync(
        MySqlConnection conn,
        MySqlTransaction tx,
        string tableName,
        Definition def,
        Dictionary<string, (string DataType, string ColumnType)> existingColumns,
        CancellationToken ct)
    {
        foreach (FieldDefinition field in def.Fields)
        {
            string colName = SanitizeIdentifier(field.Name ?? string.Empty);
            string columnDef = GetMySqlColumnDefinition(field.Type);

            if (!existingColumns.TryGetValue(colName, out (string DataType, string ColumnType) existing))
            {
                string alter = $@"ALTER TABLE `{tableName}` ADD COLUMN `{colName}` {columnDef};";
                await conn.ExecuteAsync(new CommandDefinition(alter, transaction: tx, cancellationToken: ct));
            }
            else
            {
                // Optionally check for type mismatch here; for now, only log if different base DATA_TYPE.
                string expectedDataType = columnDef.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0].ToLowerInvariant();
                if (!existing.DataType.Equals(expectedDataType, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Column type mismatch for {Table}.{Column}. Existing={Existing}, Expected={Expected}. No automatic ALTER performed.",
                        tableName, colName, existing.ColumnType, columnDef);
                }
            }
        }
    }

    private static object? ConvertValue(FieldType type, Dictionary<string, object> values, string key)
    {
        if (!values.TryGetValue(key, out object? value) || value is null)
        {
            return null;
        }

        try
        {
            switch (type)
            {
                case FieldType.Boolean:
                    // Accept bool, numeric, or string forms
                    if (value is bool b) return b;
                    if (value is sbyte sb) return sb != 0;
                    if (value is byte by) return by != 0;
                    if (value is short s) return s != 0;
                    if (value is ushort us) return us != 0;
                    if (value is int i) return i != 0;
                    if (value is uint ui) return ui != 0;
                    if (value is long l) return l != 0;
                    if (value is ulong ul) return ul != 0;
                    if (value is string bs) return bool.TryParse(bs, out bool bv) ? bv : int.TryParse(bs, out int bi) ? bi != 0 : null;
                    return null;

                case FieldType.Int:
                    if (value is int ii) return ii;
                    if (value is long ll) return checked((int)ll);
                    if (value is double dd) return checked((int)Math.Truncate(dd));
                    if (value is decimal dec) return checked((int)decimal.Truncate(dec));
                    if (value is string istn && int.TryParse(istn, NumberStyles.Any, CultureInfo.InvariantCulture, out int iv)) return iv;
                    return null;

                case FieldType.Float:
                    if (value is float f) return (double)f;
                    if (value is double d) return d;
                    if (value is decimal decc) return (double)decc;
                    if (value is string fstr && double.TryParse(fstr, NumberStyles.Any, CultureInfo.InvariantCulture, out double dv)) return dv;
                    return null;

                case FieldType.Text:
                    return value?.ToString();

                case FieldType.DateTime:
                    if (value is DateTime dt) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                    if (value is string dts && DateTime.TryParse(dts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime dtp))
                        return dtp;
                    return null;

                case FieldType.Time:
                    if (value is TimeSpan ts) return ts;
                    if (value is DateTime dtt) return dtt.TimeOfDay;
                    if (value is string tstr && TimeSpan.TryParse(tstr, CultureInfo.InvariantCulture, out TimeSpan tsp)) return tsp;
                    return null;

                case FieldType.Date:
                    if (value is DateTime dtd) return dtd.Date;
                    if (value is DateOnly dto) return dto.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                    if (value is string dstr && DateTime.TryParse(dstr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime dparsed))
                        return dparsed.Date;
                    return null;

                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    private static string GetMySqlColumnDefinition(FieldType type)
    {
        // Nullable columns to allow sparse values across entries.
        return type switch
        {
            FieldType.Boolean => "TINYINT(1) NULL",
            FieldType.Int => "INT NULL",
            FieldType.Float => "DOUBLE NULL",
            FieldType.Text => "TEXT NULL",
            FieldType.DateTime => "DATETIME(6) NULL",
            FieldType.Time => "TIME(6) NULL",
            FieldType.Date => "DATE NULL",
            _ => "TEXT NULL"
        };
    }

    private string BuildTableName(Definition def)
    {
        // Ensure uniqueness and length-safety (MySQL identifier limit 64).
        string safeName = SanitizeIdentifier(def.Name);
        string userId = SanitizeIdentifier(_userMappings[def.UserId]);
        string candidate = $"{safeName}_{userId}";
        if (candidate.Length <= 64)
        {
            return candidate;
        }

        string suffix = ShortStableSuffix(def);
        // Try shrinking the name while keeping uniqueness.
        int maxNameLen = Math.Max(1, 64 - (userId.Length + 1 + suffix.Length));
        string trimmedName = safeName.Length > maxNameLen ? safeName[..maxNameLen] : safeName;
        return $"{trimmedName}_{userId}_{suffix}";
    }

    private static string ShortStableSuffix(Definition def)
    {
        // Stable 8-char suffix from Definition.Id (or from Name+UserId if Id missing)
        string basis = !string.IsNullOrEmpty(def.Id) ? def.Id : $"{def.Name}|{def.UserId}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    private static string SanitizeIdentifier(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "col";
        }
        StringBuilder sb = new(input.Length);

        foreach (char c in input)
        {
            if (c >= 'a' && c <= 'z' ||
                c >= 'A' && c <= 'Z' ||
                c >= '0' && c <= '9' ||
                c == '_')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }
        // MySQL identifiers cannot start with a digit
        if (sb.Length == 0)
        {
            sb.Append("col");
        }
        if (char.IsDigit(sb[0]))
        {
            sb.Insert(0, 'x');
        }
        string s = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(s) ? "col" : s;
    }
}
