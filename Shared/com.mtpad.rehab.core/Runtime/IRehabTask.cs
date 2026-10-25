namespace MTPAD.Rehab.Core
{
    public interface IRehabTask
    {
        string TaskId { get; }
        bool IsRunning { get; }
        void StartTask();
        void StopTask();
        void HandleHeelStrike(HeelStrikeEvent heelStrike, double headsetReceiptTime);
    }
}
