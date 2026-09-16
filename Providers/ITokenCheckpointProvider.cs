namespace SapeagleAttendanceConnector;

public interface ITokenCheckpointProvider
{
    void CommitPendingCheckpoint();
}