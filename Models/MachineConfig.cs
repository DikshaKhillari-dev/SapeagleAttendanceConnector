namespace SapeagleAttendanceConnector.Models;

public class MachineConfig
{
    public int Id { get; set; }
    public int ComId { get; set; }
    public string MachineName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public int Port { get; set; } = 4370;
    public string Password { get; set; } = "";

    public string Username { get; set; } = "";

    public string MachineType { get; set; } = "ZKTeco";
    public bool IsActive { get; set; }
}