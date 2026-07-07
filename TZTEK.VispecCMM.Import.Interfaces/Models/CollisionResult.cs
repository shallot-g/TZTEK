namespace TZTEK.VispecCMM.Import.Interfaces.Models;

/// <summary>
/// 碰撞检测结果。
/// </summary>
public sealed class CollisionResult
{
    public bool HasCollision { get; set; }
    public IReadOnlyList<CollisionEvent> Collisions { get; set; } = [];
    public int TotalSegmentsChecked { get; set; }
}

/// <summary>
/// 碰撞事件。
/// </summary>
public sealed class CollisionEvent
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public int SegmentIndex { get; set; }
    public IReadOnlyList<string> InvolvedElementIds { get; set; } = [];
    public int Severity { get; set; }
    public GotoPoint? SuggestedAvoidancePoint { get; set; }
}
