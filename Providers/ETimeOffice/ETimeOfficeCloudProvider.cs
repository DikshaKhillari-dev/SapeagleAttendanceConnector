using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SapeagleAttendanceConnector.Services;

namespace SapeagleAttendanceConnector.ETimeOffice;

public class ETimeOfficeCloudProvider : IAttendanceProvider, ITokenCheckpointProvider
{
	private const string ApiUrl = "https://api.etimeoffice.com/api/DownloadLastPunchData";

	private readonly string _corporateId;
	private readonly string _username;
	private readonly string _password;
	private readonly string _deviceKey;
	private readonly CheckpointService _checkpoint;
	private readonly HttpClient _http;
	private string? _pendingToken;

	public string DeviceKey => _deviceKey;

	public ETimeOfficeCloudProvider(string corporateId, string username, string password, int machineConfigId, CheckpointService checkpoint)
	{
		_corporateId = corporateId;
		_username = username;
		_password = password;
		_checkpoint = checkpoint;
		_deviceKey = $"ETIMEOFFICE:{machineConfigId}";

		_http = new HttpClient();
		var authString = $"{_corporateId}:{_username}:{_password}:True";
		var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
		_http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
	}

	public bool Connect() => true;
	public void Disconnect() { }

	public List<AttendancePunch> FetchNewAttendanceRecords()
	{
		var records = new List<AttendancePunch>();
        var lastRecord = _checkpoint.GetLastToken(_deviceKey);
        if (string.IsNullOrEmpty(lastRecord) || !lastRecord.Contains('$'))
        {
            // eTimeOffice's API rejects a blank LastRecord, and when a query window has zero
            // punches it returns MaxRecord="0" (no "$") instead of a real MMyyyy$ID token.
            // Blindly persisting that bare "0" corrupts the next call's LastRecord, so treat
            // anything without "$" as "no real checkpoint" and reseed from the current month.
            lastRecord = $"{DateTime.Now:MMyyyy}$0";
            Logger.Log($"[ETimeOffice] No usable checkpoint token (was '{_checkpoint.GetLastToken(_deviceKey)}') — seeding LastRecord='{lastRecord}'.");
        }

		try
		{
			var url = $"{ApiUrl}?Empcode=ALL&LastRecord={Uri.EscapeDataString(lastRecord)}";
			var resp = _http.GetAsync(url).GetAwaiter().GetResult();
			var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (!resp.IsSuccessStatusCode)
			{
				Logger.Log($"[ETimeOffice] FetchNewAttendanceRecords: HTTP {(int)resp.StatusCode} {resp.StatusCode}. Body: {body}");
				return records;
			}

			var parsed = JsonSerializer.Deserialize<LastPunchResponse>(body,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

			if (parsed == null || parsed.Error)
			{
				Logger.Log($"[ETimeOffice] FetchNewAttendanceRecords: Error or null response. Msg={parsed?.Msg}");
				return records;
			}

			foreach (var p in parsed.PunchData)
			{
				if (!DateTime.TryParseExact(p.PunchDate, "dd/MM/yyyy HH:mm:ss",
						System.Globalization.CultureInfo.InvariantCulture,
						System.Globalization.DateTimeStyles.None, out var ts))
				{
					Logger.Log($"[ETimeOffice] Skipping unparsable PunchDate '{p.PunchDate}' Empcode={p.Empcode}");
					continue;
				}

			
				const int inOutMode = 2;

				records.Add(new AttendancePunch
				{
					EnrollNumber = p.Empcode,
					VerifyMode = 0,
					InOutMode = inOutMode,
					Timestamp = ts,
					EventId = AttendanceIdentity.ComputeEventId(_deviceKey, p.Empcode, ts, 0, inOutMode)
				});
			}

			if (!string.IsNullOrEmpty(parsed.MaxRecord))
				_pendingToken = parsed.MaxRecord;

			Logger.Log($"[ETimeOffice] FetchNewAttendanceRecords: {records.Count} new punch(es), pending MaxRecord='{parsed.MaxRecord}'.");
		}
		catch (Exception ex)
		{
			Logger.Log($"[ETimeOffice] FetchNewAttendanceRecords: exception - {ex.Message}");
		}

		return records;
	}

	public void CommitPendingCheckpoint()
	{
		if (_pendingToken != null)
		{
			_checkpoint.UpdateLastToken(_deviceKey, _pendingToken);
			_pendingToken = null;
		}
	}

	public List<Models.MachineEmployee> ReadExistingEmployees() => new();
	public bool CreateEmployee(string enrollNumber, string employeeName, string? fallbackNumericId = null) => false;
	public bool DeleteEmployee(string enrollNumber) => false;

	public void Dispose() => _http.Dispose();

	private class LastPunchResponse
	{
		public bool Error { get; set; }
		public string Msg { get; set; } = "";
		public bool IsAdmin { get; set; }
		public List<PunchItem> PunchData { get; set; } = new();
		public string MaxRecord { get; set; } = "";
	}

	private class PunchItem
	{
		public string Name { get; set; } = "";
		public string Empcode { get; set; } = "";
		public string PunchDate { get; set; } = "";
	}
}