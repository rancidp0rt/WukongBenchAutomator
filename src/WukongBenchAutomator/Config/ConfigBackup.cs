using WukongBenchAutomator.Infrastructure;

namespace WukongBenchAutomator.Config;

// Если утилиту убьют посреди прогона, копия останется и вернётся при следующем запуске.
internal sealed class ConfigBackup(string configPath)
{
    public string ConfigPath { get; } = configPath;
    public string BackupPath { get; } = configPath + ".wukongbench-backup";

    public bool Exists => File.Exists(BackupPath);

    public void Create() => File.Copy(ConfigPath, BackupPath, overwrite: true);

    public bool Restore()
    {
        if (!Exists)
        {
            return false;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(BackupPath, ConfigPath, overwrite: true);
                File.Delete(BackupPath);
                return true;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                Log.Error($"Не удалось восстановить {ConfigPath} из {BackupPath}: {ex.Message}");
                return false;
            }
        }
    }
}
