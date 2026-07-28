using SapeagleAttendanceConnector.Models;
using SapeagleAttendanceConnector.SBXPC;

namespace SapeagleAttendanceConnector.Services;

public static class MachineFactory
{
    public static IAttendanceProvider Create(MachineConfig machine, int machineNumber, CheckpointService checkpoint)
    {
        Logger.Log($"[MachineFactory] Create: MachineConfig.Id={machine.Id}, MachineName='{machine.MachineName}', " +
                   $"DeviceId(raw)='{machine.DeviceId}' -> machineNumber={machineNumber}, MachineType='{machine.MachineType}', " +
                   $"IpAddress={machine.IpAddress}:{machine.Port}");

        IAttendanceProvider provider = machine.MachineType?.Trim().ToUpperInvariant() switch
        {
            "ZKTECO" => new ZKTecoProvider(
                machine.IpAddress, machine.Port, machineNumber,
                int.TryParse(machine.Password, out var zkpw) ? zkpw : 0,
                checkpoint),

            "SBXPC" => new SBXPCProvider(
                machine.IpAddress, machine.Port, machineNumber,
                int.TryParse(machine.Password, out var pw) ? pw : 0,
                checkpoint),

            _ => throw new NotSupportedException($"Unknown MachineType '{machine.MachineType}'")
        };

        Logger.Log($"[MachineFactory] Create: provider '{provider.GetType().Name}' created for machineNumber={machineNumber}.");
        return provider;
    }
}