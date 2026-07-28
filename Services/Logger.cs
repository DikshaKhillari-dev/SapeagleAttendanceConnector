using System.Text;

namespace SapeagleAttendanceConnector.Services;

public static class Logger
{
	private static readonly object _lock = new();
	private static readonly string _logDir;
	private static readonly string _logPath;

	static Logger()
	{
		_logDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
			"SapeagleAttendanceConnector", "logs");
		Directory.CreateDirectory(_logDir);
		_logPath = Path.Combine(_logDir, "log.txt");
	}

	public static void Log(string message)
	{
		try
		{
			lock (_lock)
			{
				var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}{Environment.NewLine}";
				File.AppendAllText(_logPath, line, Encoding.UTF8);
				RotateIfTooBig();
			}
		}
		catch { /* logging must never crash the app */ }
	}

	private static void RotateIfTooBig()
	{
		var info = new FileInfo(_logPath);
		if (info.Exists && info.Length > 5 * 1024 * 1024) // 5 MB cap
		{
			var backupPath = Path.Combine(_logDir, "log.old.txt");
			File.Copy(_logPath, backupPath, overwrite: true);
			File.WriteAllText(_logPath, "");
		}
	}
}