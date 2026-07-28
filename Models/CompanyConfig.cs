namespace SapeagleAttendanceConnector.Models;

public class ActivatedMachine
{
    public int ComId { get; set; }
    public string CompanyCode { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public int MachineId { get; set; }
    public string MachineName { get; set; } = "";
    public string ActivationKey { get; set; } = "";
    public DateTime ActivatedOn { get; set; }
}

public class CompanyConfig
{
    public List<ActivatedMachine> Machines { get; set; } = new();
    public bool IsActivated => Machines.Count > 0;
}