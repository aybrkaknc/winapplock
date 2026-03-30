using System.Text.Json;
using Microsoft.Data.Sqlite;
using WinAppLock.Core.Models;

namespace WinAppLock.Core.Data;

/// <summary>
/// SQLite veritabanı üzerinden kilitli uygulama ve ayar yönetimi.
/// Veritabanı %APPDATA%\WinAppLock\ altında oluşturulur.
/// Tablo yapıları ilk çalıştırmada otomatik oluşturulur.
/// </summary>
public class AppDatabase : IDisposable
{
    private readonly string _connectionString;

    /// <summary>
    /// Veritabanını başlatır. Dosya yoksa oluşturur, tabloları kontrol eder.
    /// </summary>
    /// <param name="dbPath">
    /// Veritabanı dosyasının tam yolu. Null ise varsayılan konum kullanılır:
    /// %APPDATA%\WinAppLock\winapplock.db
    /// </param>
    public AppDatabase(string? dbPath = null)
    {
        dbPath ??= GetDefaultDbPath();

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = $"Data Source={dbPath}";
        InitializeTables();
    }

    /// <summary>
    /// Veritabanı tablolarını oluşturur (yoksa).
    /// </summary>
    private void InitializeTables()
    {
        using var connection = CreateConnection();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS LockedApps (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DisplayName TEXT NOT NULL,
                IdentityJson TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CustomPasswordHash TEXT,
                RelockBehavior INTEGER NOT NULL DEFAULT 0,
                RelockTimeMinutes INTEGER NOT NULL DEFAULT 15,
                LockChildProcesses INTEGER NOT NULL DEFAULT 0,
                IconBase64 TEXT,
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AccessLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AppName TEXT NOT NULL,
                AttemptTime TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                IsSuccess INTEGER NOT NULL,
                IpAddress TEXT
            );
        """;
        command.ExecuteNonQuery();
    }

    // ─── Kilitli Uygulama İşlemleri ───

    /// <summary>
    /// Yeni kilitli uygulama kaydı ekler.
    /// </summary>
    /// <param name="app">Eklenecek uygulama modeli</param>
    /// <returns>Oluşturulan kaydın ID'si</returns>
    public int AddLockedApp(LockedApp app)
    {
        using var connection = CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO LockedApps (DisplayName, IdentityJson, IsEnabled, CustomPasswordHash, 
                                    RelockBehavior, RelockTimeMinutes, LockChildProcesses, IconBase64, CreatedAt)
            VALUES ($displayName, $identityJson, $isEnabled, $customPwHash,
                    $relockBehavior, $relockTime, $lockChild, $icon, $createdAt);
            SELECT last_insert_rowid();
        """;

        command.Parameters.AddWithValue("$displayName", app.DisplayName);
        command.Parameters.AddWithValue("$identityJson", JsonSerializer.Serialize(app.Identity));
        command.Parameters.AddWithValue("$isEnabled", app.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$customPwHash", (object?)app.CustomPasswordHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$relockBehavior", (int)app.RelockBehavior);
        command.Parameters.AddWithValue("$relockTime", app.RelockTimeMinutes);
        command.Parameters.AddWithValue("$lockChild", app.LockChildProcesses ? 1 : 0);
        command.Parameters.AddWithValue("$icon", (object?)app.IconBase64 ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", app.CreatedAt.ToString("o"));

        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// Tüm kilitli uygulamaları getirir.
    /// </summary>
    /// <returns>Kilitli uygulama listesi</returns>
    public List<LockedApp> GetAllLockedApps()
    {
        using var connection = CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM LockedApps ORDER BY CreatedAt DESC";

        var apps = new List<LockedApp>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            apps.Add(MapLockedApp(reader));
        }
        return apps;
    }

    /// <summary>
    /// Aktif (enabled) kilitli uygulamaları getirir.
    /// Service bu metodu kullanarak sadece aktif kilitleri izler.
    /// </summary>
    /// <returns>Aktif kilitli uygulama listesi</returns>
    public List<LockedApp> GetEnabledLockedApps()
    {
        using var connection = CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM LockedApps WHERE IsEnabled = 1";

        var apps = new List<LockedApp>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            apps.Add(MapLockedApp(reader));
        }
        return apps;
    }

    /// <summary>
    /// Kilitli uygulamayı günceller (toggle, relock ayarları vb.).
    /// </summary>
    /// <param name="app">Güncellenecek uygulama</param>
    public void UpdateLockedApp(LockedApp app)
    {
        using var connection = CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LockedApps 
            SET DisplayName = $displayName, IdentityJson = $identityJson, IsEnabled = $isEnabled,
                CustomPasswordHash = $customPwHash, RelockBehavior = $relockBehavior, 
                RelockTimeMinutes = $relockTime, LockChildProcesses = $lockChild, IconBase64 = $icon
            WHERE Id = $id
        """;

        command.Parameters.AddWithValue("$id", app.Id);
        command.Parameters.AddWithValue("$displayName", app.DisplayName);
        command.Parameters.AddWithValue("$identityJson", JsonSerializer.Serialize(app.Identity));
        command.Parameters.AddWithValue("$isEnabled", app.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$customPwHash", (object?)app.CustomPasswordHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$relockBehavior", (int)app.RelockBehavior);
        command.Parameters.AddWithValue("$relockTime", app.RelockTimeMinutes);
        command.Parameters.AddWithValue("$lockChild", app.LockChildProcesses ? 1 : 0);
        command.Parameters.AddWithValue("$icon", (object?)app.IconBase64 ?? DBNull.Value);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Kilitli uygulamayı siler.
    /// </summary>
    /// <param name="id">Silinecek uygulamanın ID'si</param>
    public void RemoveLockedApp(int id)
    {
        using var connection = CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM LockedApps WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    // ─── Ayar İşlemleri ───

    /// <summary>
    /// Uygulama ayarlarını getirir. Kayıt yoksa varsayılanları döner.
    /// </summary>
    /// <returns>AppSettings nesnesi</returns>
    public AppSettings GetSettings()
    {
        var json = GetSettingValue("app_settings");
        if (string.IsNullOrEmpty(json))
            return new AppSettings();

        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    /// <summary>
    /// Uygulama ayarlarını kaydeder.
    /// </summary>
    /// <param name="settings">Kaydedilecek ayarlar</param>
    public void SaveSettings(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings);
        SetSettingValue("app_settings", json);
    }

    // ─── Log İşlemleri ───

    /// <summary>
    /// Erişim denemesini loglar.
    /// </summary>
    /// <param name="appName">Denenen uygulama adı</param>
    /// <param name="isSuccess">Başarılı mı?</param>
    public void LogAccessAttempt(string appName, bool isSuccess)
    {
        using var connection = CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AccessLogs (AppName, IsSuccess) VALUES ($appName, $isSuccess)
        """;
        command.Parameters.AddWithValue("$appName", appName);
        command.Parameters.AddWithValue("$isSuccess", isSuccess ? 1 : 0);
        command.ExecuteNonQuery();
    }

    // ─── Yardımcı Metodlar ───

    private string? GetSettingValue(string key)
    {
        using var connection = CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar()?.ToString();
    }

    private void SetSettingValue(string key, string value)
    {
        using var connection = CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO Settings (Key, Value) VALUES ($key, $value)
        """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static LockedApp MapLockedApp(SqliteDataReader reader)
    {
        var identityJson = reader.GetString(reader.GetOrdinal("IdentityJson"));
        var identity = JsonSerializer.Deserialize<AppIdentity>(identityJson) ?? new AppIdentity();

        return new LockedApp
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            DisplayName = reader.GetString(reader.GetOrdinal("DisplayName")),
            Identity = identity,
            IsEnabled = reader.GetInt32(reader.GetOrdinal("IsEnabled")) == 1,
            CustomPasswordHash = reader.IsDBNull(reader.GetOrdinal("CustomPasswordHash"))
                ? null : reader.GetString(reader.GetOrdinal("CustomPasswordHash")),
            RelockBehavior = (RelockBehavior)reader.GetInt32(reader.GetOrdinal("RelockBehavior")),
            RelockTimeMinutes = reader.GetInt32(reader.GetOrdinal("RelockTimeMinutes")),
            LockChildProcesses = reader.GetInt32(reader.GetOrdinal("LockChildProcesses")) == 1,
            IconBase64 = reader.IsDBNull(reader.GetOrdinal("IconBase64"))
                ? null : reader.GetString(reader.GetOrdinal("IconBase64"))
        };
    }

    private static string GetDefaultDbPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "WinAppLock", "winapplock.db");
    }

    public void Dispose()
    {
        // SQLite bağlantıları her metodda açılıp kapatılıyor, dispose'da ek işlem yok
        GC.SuppressFinalize(this);
    }
}
